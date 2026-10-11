using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.AccountAccess;

public enum AccountAccessGateMode
{
    Off,
    Enforce
}

/// <summary>
/// enforce modunda eksik ya da hatalı her ayar açılışı durdurur; kapı sessizce açık kalmamalı.
/// </summary>
public sealed class AccountAccessGateOptions
{
    public AccountAccessGateMode Mode { get; private init; }
    public string Endpoint { get; private init; } = "";
    public string Username { get; private init; } = "";
    public string Password { get; private init; } = "";
    public int Database { get; private init; }
    public bool UseTls { get; private init; }
    public string CaCertificateFile { get; private init; } = "";
    public string ClientCertificateFile { get; private init; } = "";
    public string ClientKeyFile { get; private init; } = "";
    public int OperationTimeoutMilliseconds { get; private init; }
    public int RetryAfterSeconds { get; private init; }

    public static AccountAccessGateOptions FromConfiguration(IConfiguration configuration)
    {
        string? Read(string name, string option) =>
            Environment.GetEnvironmentVariable(name) ?? configuration[$"AccountAccessGate:{option}"];

        string Require(string name, string option) =>
            Read(name, option) is { } value && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new InvalidOperationException($"{name} is required when ACCOUNT_ACCESS_GATE_MODE=enforce.");

        string RequireFile(string name, string option)
        {
            var path = Require(name, option);
            if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException($"{name} must be an absolute path.");
            if (!File.Exists(path)) throw new InvalidOperationException($"{name} must point to a readable file.");
            return path;
        }

        var mode = Read("ACCOUNT_ACCESS_GATE_MODE", "Mode")?.Trim().ToLowerInvariant() switch
        {
            "off" => AccountAccessGateMode.Off,
            "enforce" => AccountAccessGateMode.Enforce,
            _ => throw new InvalidOperationException("ACCOUNT_ACCESS_GATE_MODE must be exactly 'off' or 'enforce'.")
        };

        var timeout = Bounded(Read("ACCOUNT_ACCESS_REDIS_OPERATION_TIMEOUT_MS", "OperationTimeoutMilliseconds") ?? "200", "ACCOUNT_ACCESS_REDIS_OPERATION_TIMEOUT_MS", 50, 2_000);
        var retryAfter = Bounded(Read("ACCOUNT_ACCESS_GATE_RETRY_AFTER_SECONDS", "RetryAfterSeconds") ?? "1", "ACCOUNT_ACCESS_GATE_RETRY_AFTER_SECONDS", 1, 30);

        if (mode == AccountAccessGateMode.Off)
            return new AccountAccessGateOptions { Mode = mode, OperationTimeoutMilliseconds = timeout, RetryAfterSeconds = retryAfter };

        var endpoint = Require("ACCOUNT_ACCESS_REDIS_ENDPOINT", "Endpoint");
        ParseEndpoint(endpoint);

        var useTls = bool.TryParse(Require("ACCOUNT_ACCESS_REDIS_TLS", "Tls"), out var tls)
            ? tls
            : throw new InvalidOperationException("ACCOUNT_ACCESS_REDIS_TLS must be 'true' or 'false'.");

        var options = new AccountAccessGateOptions
        {
            Mode = mode,
            Endpoint = endpoint,
            Username = Require("ACCOUNT_ACCESS_REDIS_USERNAME", "Username"),
            Password = Require("ACCOUNT_ACCESS_REDIS_PASSWORD", "Password"),
            Database = Bounded(Require("ACCOUNT_ACCESS_REDIS_DATABASE", "Database"), "ACCOUNT_ACCESS_REDIS_DATABASE", 0, int.MaxValue),
            UseTls = useTls,
            CaCertificateFile = useTls ? RequireFile("ACCOUNT_ACCESS_REDIS_CA_CERT_FILE", "CaCertificateFile") : "",
            ClientCertificateFile = useTls ? RequireFile("ACCOUNT_ACCESS_REDIS_TLS_CERT_FILE", "ClientCertificateFile") : "",
            ClientKeyFile = useTls ? RequireFile("ACCOUNT_ACCESS_REDIS_TLS_KEY_FILE", "ClientKeyFile") : "",
            OperationTimeoutMilliseconds = timeout,
            RetryAfterSeconds = retryAfter
        };

        if (useTls) ValidateTlsMaterial(options);

        return options;
    }

    internal ConfigurationOptions ToRedisConfiguration()
    {
        var (host, port) = ParseEndpoint(Endpoint);

        var redis = new ConfigurationOptions
        {
            User = Username,
            Password = Password,
            DefaultDatabase = Database,
            Ssl = UseTls,
            SslHost = UseTls ? host : null,
            ClientName = "forms-account-access-gate",
            AbortOnConnectFail = false,
            AllowAdmin = false,
            ConnectRetry = 0,
            ConnectTimeout = OperationTimeoutMilliseconds,
            AsyncTimeout = OperationTimeoutMilliseconds,
            SyncTimeout = OperationTimeoutMilliseconds
        };
        redis.EndPoints.Add(host, port);

        if (!UseTls) return redis;

        var caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(CaCertificateFile));
        var clientCertificate = X509Certificate2.CreateFromPemFile(ClientCertificateFile, ClientKeyFile);

        // Yalnız kendi CA'mız güvenilir; sistem kök sertifikalarına düşülmez.
        redis.SslClientAuthenticationOptions = targetHost =>
        {
            var chainPolicy = new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                RevocationMode = X509RevocationMode.NoCheck
            };
            chainPolicy.CustomTrustStore.Add(caCertificate);

            return new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificates = new X509CertificateCollection { clientCertificate },
                CertificateChainPolicy = chainPolicy
            };
        };

        return redis;
    }

    private static void ValidateTlsMaterial(AccountAccessGateOptions options)
    {
        try
        {
            using var caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(options.CaCertificateFile));
            using var clientCertificate = X509Certificate2.CreateFromPemFile(options.ClientCertificateFile, options.ClientKeyFile);

            if (!clientCertificate.HasPrivateKey)
                throw new InvalidOperationException("ACCOUNT_ACCESS_REDIS_TLS_CERT_FILE must match ACCOUNT_ACCESS_REDIS_TLS_KEY_FILE.");
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException("Account access Redis mTLS files must contain valid PEM certificates and a matching private key.", exception);
        }
    }

    private static int Bounded(string value, string name, int minimum, int maximum) =>
        int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum
            ? parsed
            : throw new InvalidOperationException($"{name} must be an integer between {minimum} and {maximum}.");

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        if (endpoint.Any(char.IsWhiteSpace) || endpoint.Contains(',') || endpoint.Contains("//"))
            throw new InvalidOperationException("ACCOUNT_ACCESS_REDIS_ENDPOINT must contain one host:port endpoint, not a connection string.");

        var separator = endpoint.LastIndexOf(':');
        var host = separator > 0 ? endpoint[..separator].Trim('[', ']') : "";

        if (string.IsNullOrWhiteSpace(host) || !int.TryParse(endpoint[(separator + 1)..], out var port) || port is < 1 or > 65_535)
            throw new InvalidOperationException("ACCOUNT_ACCESS_REDIS_ENDPOINT must contain one host:port endpoint.");

        return (host, port);
    }
}
