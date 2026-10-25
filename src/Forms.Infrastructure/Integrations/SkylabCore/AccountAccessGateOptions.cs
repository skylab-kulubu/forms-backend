using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

/// <summary>
/// enforce modunda eksik ya da hatalı her ayar açılışı durdurur; kapı sessizce açık kalmamalı.
/// </summary>
internal sealed class AccountAccessGateOptions
{
    public const string SectionName = "AccountAccessGate";

    public string? Mode { get; init; }

    public string? Endpoint { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public int? Database { get; init; }
    public bool? Tls { get; init; }
    public string? CaCertificateFile { get; init; }
    public string? ClientCertificateFile { get; init; }
    public string? ClientKeyFile { get; init; }
    public int? OperationTimeoutMilliseconds { get; init; }
    public int? RetryAfterSeconds { get; init; }

    public bool Enforces => string.Equals(Mode?.Trim(), "enforce", StringComparison.OrdinalIgnoreCase);

    internal int OperationTimeout => OperationTimeoutMilliseconds ?? 200;

    public int RetryAfter => RetryAfterSeconds ?? 1;

    internal ConfigurationOptions ToRedisConfiguration()
    {
        var (host, port) = ParseEndpoint(Endpoint!)!.Value;
        var useTls = Tls == true;

        var redis = new ConfigurationOptions
        {
            User = Username,
            Password = Password,
            DefaultDatabase = Database,
            Ssl = useTls,
            SslHost = useTls ? host : null,
            ClientName = "forms-account-access-gate",
            AbortOnConnectFail = false,
            AllowAdmin = false,
            ConnectRetry = 0,
            ConnectTimeout = OperationTimeout,
            AsyncTimeout = OperationTimeout,
            SyncTimeout = OperationTimeout
        };
        redis.EndPoints.Add(host, port);

        if (!useTls) return redis;

        var caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(CaCertificateFile!));
        var clientCertificate = X509Certificate2.CreateFromPemFile(ClientCertificateFile!, ClientKeyFile);

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

    internal static (string Host, int Port)? ParseEndpoint(string endpoint)
    {
        if (endpoint.Any(char.IsWhiteSpace) || endpoint.Contains(',') || endpoint.Contains("//")) return null;

        var separator = endpoint.LastIndexOf(':');
        var host = separator > 0 ? endpoint[..separator].Trim('[', ']') : "";

        return string.IsNullOrWhiteSpace(host) || !int.TryParse(endpoint[(separator + 1)..], out var port) || port is < 1 or > 65_535
            ? null
            : (host, port);
    }
}

internal sealed class AccountAccessGateOptionsValidator : IValidateOptions<AccountAccessGateOptions>
{
    public ValidateOptionsResult Validate(string? name, AccountAccessGateOptions options)
    {
        var mode = options.Mode?.Trim().ToLowerInvariant();
        if (mode is not ("off" or "enforce"))
            return ValidateOptionsResult.Fail("AccountAccessGate:Mode must be exactly 'off' or 'enforce'.");

        if (options.OperationTimeoutMilliseconds is < 50 or > 2_000)
            return ValidateOptionsResult.Fail("AccountAccessGate:OperationTimeoutMilliseconds must be between 50 and 2000.");

        if (options.RetryAfterSeconds is < 1 or > 30)
            return ValidateOptionsResult.Fail("AccountAccessGate:RetryAfterSeconds must be between 1 and 30.");

        if (mode == "off") return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Endpoint))
            failures.Add(Required("Endpoint"));
        else if (AccountAccessGateOptions.ParseEndpoint(options.Endpoint) is null)
            failures.Add("AccountAccessGate:Endpoint must contain one host:port endpoint, not a connection string.");

        if (string.IsNullOrWhiteSpace(options.Username)) failures.Add(Required("Username"));
        if (string.IsNullOrWhiteSpace(options.Password)) failures.Add(Required("Password"));
        if (options.Database is not >= 0) failures.Add(Required("Database"));
        if (options.Tls is null) failures.Add("AccountAccessGate:Tls must be 'true' or 'false' when the gate enforces.");

        if (options.Tls == true)
        {
            foreach (var (key, path) in new[] { ("CaCertificateFile", options.CaCertificateFile), ("ClientCertificateFile", options.ClientCertificateFile), ("ClientKeyFile", options.ClientKeyFile) })
            {
                if (string.IsNullOrWhiteSpace(path)) failures.Add(Required(key));
                else if (!Path.IsPathFullyQualified(path)) failures.Add($"AccountAccessGate:{key} must be an absolute path.");
                else if (!File.Exists(path)) failures.Add($"AccountAccessGate:{key} must point to a readable file.");
            }

            if (failures.Count == 0 && TlsMaterialError(options) is { } error) failures.Add(error);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static string Required(string key) => $"AccountAccessGate:{key} is required when AccountAccessGate:Mode is enforce.";

    private static string? TlsMaterialError(AccountAccessGateOptions options)
    {
        try
        {
            using var caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(options.CaCertificateFile!));
            using var clientCertificate = X509Certificate2.CreateFromPemFile(options.ClientCertificateFile!, options.ClientKeyFile);

            return clientCertificate.HasPrivateKey ? null : "AccountAccessGate:ClientCertificateFile must match AccountAccessGate:ClientKeyFile.";
        }
        catch (Exception)
        {
            return "The account access gate's mTLS files must contain valid PEM certificates and a matching private key.";
        }
    }
}
