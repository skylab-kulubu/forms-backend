using Microsoft.Extensions.Configuration;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.AccountAccess;

public enum AccountAccessGateMode
{
    Off,
    Enforce
}

public sealed class AccountAccessGateOptions
{
    public AccountAccessGateMode Mode { get; private init; }
    public string? Endpoint { get; private init; }
    public string? Username { get; private init; }
    public string? Password { get; private init; }
    public int Database { get; private init; }
    public bool UseTls { get; private init; }
    public string? CaCertificateFile { get; private init; }
    public string? ClientCertificateFile { get; private init; }
    public string? ClientKeyFile { get; private init; }
    public int OperationTimeoutMilliseconds { get; private init; }
    public int RetryAfterSeconds { get; private init; }

    public static AccountAccessGateOptions FromConfiguration(IConfiguration configuration)
    {
        var modeValue = Read(configuration, "ACCOUNT_ACCESS_GATE_MODE", "Mode");
        if (string.IsNullOrWhiteSpace(modeValue) ||
            !Enum.TryParse<AccountAccessGateMode>(modeValue, ignoreCase: true, out var mode) ||
            mode is not (AccountAccessGateMode.Off or AccountAccessGateMode.Enforce))
        {
            throw new InvalidOperationException(
                "ACCOUNT_ACCESS_GATE_MODE must be exactly 'off' or 'enforce'.");
        }

        var timeout = ParseBoundedInt(
            Read(configuration, "ACCOUNT_ACCESS_REDIS_OPERATION_TIMEOUT_MS", "OperationTimeoutMilliseconds") ?? "200",
            "ACCOUNT_ACCESS_REDIS_OPERATION_TIMEOUT_MS",
            minimum: 50,
            maximum: 2_000);
        var retryAfter = ParseBoundedInt(
            Read(configuration, "ACCOUNT_ACCESS_GATE_RETRY_AFTER_SECONDS", "RetryAfterSeconds") ?? "1",
            "ACCOUNT_ACCESS_GATE_RETRY_AFTER_SECONDS",
            minimum: 1,
            maximum: 30);

        if (mode == AccountAccessGateMode.Off)
        {
            return new AccountAccessGateOptions
            {
                Mode = mode,
                OperationTimeoutMilliseconds = timeout,
                RetryAfterSeconds = retryAfter
            };
        }

        var endpoint = Require(configuration, "ACCOUNT_ACCESS_REDIS_ENDPOINT", "Endpoint");
        var username = Require(configuration, "ACCOUNT_ACCESS_REDIS_USERNAME", "Username");
        var password = Require(configuration, "ACCOUNT_ACCESS_REDIS_PASSWORD", "Password");
        var database = ParseBoundedInt(
            Require(configuration, "ACCOUNT_ACCESS_REDIS_DATABASE", "Database"),
            "ACCOUNT_ACCESS_REDIS_DATABASE",
            minimum: 0,
            maximum: int.MaxValue);
        var tlsValue = Require(configuration, "ACCOUNT_ACCESS_REDIS_TLS", "Tls");
        if (!bool.TryParse(tlsValue, out var useTls))
        {
            throw new InvalidOperationException("ACCOUNT_ACCESS_REDIS_TLS must be 'true' or 'false'.");
        }

        string? caCertificateFile = null;
        string? clientCertificateFile = null;
        string? clientKeyFile = null;
        if (useTls)
        {
            caCertificateFile = RequireAbsoluteFile(
                configuration,
                "ACCOUNT_ACCESS_REDIS_CA_CERT_FILE",
                "CaCertificateFile");
            clientCertificateFile = RequireAbsoluteFile(
                configuration,
                "ACCOUNT_ACCESS_REDIS_TLS_CERT_FILE",
                "ClientCertificateFile");
            clientKeyFile = RequireAbsoluteFile(
                configuration,
                "ACCOUNT_ACCESS_REDIS_TLS_KEY_FILE",
                "ClientKeyFile");
            ValidateTlsMaterial(caCertificateFile, clientCertificateFile, clientKeyFile);
        }

        ParseEndpoint(endpoint);

        return new AccountAccessGateOptions
        {
            Mode = mode,
            Endpoint = endpoint,
            Username = username,
            Password = password,
            Database = database,
            UseTls = useTls,
            CaCertificateFile = caCertificateFile,
            ClientCertificateFile = clientCertificateFile,
            ClientKeyFile = clientKeyFile,
            OperationTimeoutMilliseconds = timeout,
            RetryAfterSeconds = retryAfter
        };
    }

    internal ConfigurationOptions ToRedisConfiguration()
    {
        if (Mode != AccountAccessGateMode.Enforce)
            throw new InvalidOperationException("The access-gate Redis client is disabled.");

        var (host, port) = ParseEndpoint(Endpoint!);
        var options = new ConfigurationOptions
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
        options.EndPoints.Add(host, port);
        if (UseTls)
        {
            var caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(CaCertificateFile!));
            var clientCertificate = X509Certificate2.CreateFromPemFile(
                ClientCertificateFile!,
                ClientKeyFile!);
            options.SslClientAuthenticationOptions = targetHost =>
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
        }
        return options;
    }

    private static string RequireAbsoluteFile(
        IConfiguration configuration,
        string environmentName,
        string optionName)
    {
        var path = Require(configuration, environmentName, optionName);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException($"{environmentName} must be an absolute path.");
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"{environmentName} must point to a readable file.");
        }

        return path;
    }

    private static void ValidateTlsMaterial(
        string caCertificateFile,
        string clientCertificateFile,
        string clientKeyFile)
    {
        try
        {
            using var caCertificate = X509Certificate2.CreateFromPem(
                File.ReadAllText(caCertificateFile));
            using var clientCertificate = X509Certificate2.CreateFromPemFile(
                clientCertificateFile,
                clientKeyFile);
            if (!clientCertificate.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "ACCOUNT_ACCESS_REDIS_TLS_CERT_FILE must match ACCOUNT_ACCESS_REDIS_TLS_KEY_FILE.");
            }
        }
        catch (Exception exception) when (exception is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "Account access Redis mTLS files must contain valid PEM certificates and a matching private key.",
                exception);
        }
    }

    private static string? Read(IConfiguration configuration, string environmentName, string optionName)
    {
        return Environment.GetEnvironmentVariable(environmentName)
            ?? configuration[$"AccountAccessGate:{optionName}"];
    }

    private static string Require(
        IConfiguration configuration,
        string environmentName,
        string optionName)
    {
        var value = Read(configuration, environmentName, optionName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{environmentName} is required when ACCOUNT_ACCESS_GATE_MODE=enforce.");
        }

        return value;
    }

    private static int ParseBoundedInt(string value, string name, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"{name} must be an integer between {minimum} and {maximum}.");
        }

        return parsed;
    }

    private static (string Host, int Port) ParseEndpoint(string endpoint)
    {
        if (endpoint.Any(char.IsWhiteSpace) || endpoint.Contains(',') || endpoint.Contains("//"))
        {
            throw new InvalidOperationException(
                "ACCOUNT_ACCESS_REDIS_ENDPOINT must contain one host:port endpoint, not a connection string.");
        }

        var separator = endpoint.LastIndexOf(':');
        if (separator <= 0 || separator == endpoint.Length - 1 ||
            !int.TryParse(endpoint[(separator + 1)..], out var port) || port is < 1 or > 65_535)
        {
            throw new InvalidOperationException(
                "ACCOUNT_ACCESS_REDIS_ENDPOINT must contain one host:port endpoint.");
        }

        var host = endpoint[..separator].Trim('[', ']');
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "ACCOUNT_ACCESS_REDIS_ENDPOINT must contain one host:port endpoint.");
        }

        return (host, port);
    }
}
