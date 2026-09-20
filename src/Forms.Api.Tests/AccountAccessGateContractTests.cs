using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Skylab.Forms.Api.Auth;
using Skylab.Forms.Infrastructure.AccountAccess;
using StackExchange.Redis;
using Xunit;

namespace Skylab.Forms.Api.Tests;

public sealed class AccountAccessGateContractTests : IDisposable
{
    private readonly string tlsDirectory;
    private readonly string caCertificateFile;
    private readonly string clientCertificateFile;
    private readonly string clientKeyFile;

    public AccountAccessGateContractTests()
    {
        tlsDirectory = Path.Combine(Path.GetTempPath(), $"forms-access-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tlsDirectory);
        caCertificateFile = Path.Combine(tlsDirectory, "ca.crt");
        clientCertificateFile = Path.Combine(tlsDirectory, "forms.crt");
        clientKeyFile = Path.Combine(tlsDirectory, "forms.key");
        WriteTlsMaterial(caCertificateFile, clientCertificateFile, clientKeyFile);
    }

    public void Dispose()
    {
        Directory.Delete(tlsDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Golden_digest_matches_the_cross_language_contract()
    {
        Assert.Equal(
            FormsJwtAuthenticationExtensions.ExactIssuer,
            AccountAccessGateContract.ExactIssuer);

        var digest = AccountAccessGateContract.DigestSubject(
            AccountAccessGateContract.GoldenSubject);

        Assert.Equal(AccountAccessGateContract.GoldenDigest, digest);
        Assert.Equal(digest, digest.ToLowerInvariant());
        Assert.Equal(64, digest.Length);
        Assert.Equal(
            AccountAccessGateContract.MarkerPrefix + AccountAccessGateContract.GoldenDigest,
            AccountAccessGateContract.MarkerKey(AccountAccessGateContract.GoldenSubject));
        Assert.DoesNotContain(AccountAccessGateContract.GoldenSubject, digest, StringComparison.Ordinal);
        Assert.DoesNotContain(
            AccountAccessGateContract.GoldenSubject,
            AccountAccessGateContract.MarkerKey(AccountAccessGateContract.GoldenSubject),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_requires_an_explicit_gate_mode()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => AccountAccessGateOptions.FromConfiguration(Configuration([])));

        Assert.Contains("off", exception.Message, StringComparison.Ordinal);
        Assert.Contains("enforce", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("observe")]
    [InlineData("allow")]
    [InlineData("")]
    public void Startup_rejects_unknown_gate_modes(string mode)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AccountAccessGate:Mode"] = mode
        });

        var exception = Assert.Throws<InvalidOperationException>(
            () => AccountAccessGateOptions.FromConfiguration(configuration));

        Assert.Contains("off", exception.Message, StringComparison.Ordinal);
        Assert.Contains("enforce", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Off_mode_needs_no_Redis_configuration()
    {
        var options = AccountAccessGateOptions.FromConfiguration(
            Configuration(new Dictionary<string, string?>
            {
                ["AccountAccessGate:Mode"] = "off"
            }));

        Assert.Equal(AccountAccessGateMode.Off, options.Mode);
        Assert.Null(options.Endpoint);
        Assert.Null(options.Username);
        Assert.Null(options.Password);
    }

    [Fact]
    public void Enforce_mode_registers_a_separately_named_Redis_connection()
    {
        var services = new ServiceCollection();

        services.AddAccountAccessGate(Configuration(CompleteSettings()));

        var redisDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));
        Assert.True(redisDescriptor.IsKeyedService);
        Assert.Equal(AccountAccessGateContract.RedisConnectionName, redisDescriptor.ServiceKey);
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(IConnectionMultiplexer) && !descriptor.IsKeyedService);
    }

    [Theory]
    [InlineData("Endpoint")]
    [InlineData("Username")]
    [InlineData("Password")]
    [InlineData("Database")]
    [InlineData("Tls")]
    [InlineData("CaCertificateFile")]
    [InlineData("ClientCertificateFile")]
    [InlineData("ClientKeyFile")]
    public void Enforce_mode_requires_every_connection_setting(string missingSetting)
    {
        var settings = CompleteSettings();
        settings.Remove($"AccountAccessGate:{missingSetting}");

        var exception = Assert.Throws<InvalidOperationException>(
            () => AccountAccessGateOptions.FromConfiguration(Configuration(settings)));

        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tls_uses_the_private_CA_and_client_certificate()
    {
        var options = AccountAccessGateOptions.FromConfiguration(Configuration(CompleteSettings()));
        var redis = options.ToRedisConfiguration();

        Assert.NotNull(redis.SslClientAuthenticationOptions);
        var tls = redis.SslClientAuthenticationOptions("redis.internal");
        Assert.Equal("redis.internal", tls.TargetHost);
        Assert.Equal(SslProtocols.Tls12 | SslProtocols.Tls13, tls.EnabledSslProtocols);
        Assert.Single(tls.ClientCertificates!);
        Assert.True(((X509Certificate2)tls.ClientCertificates![0]!).HasPrivateKey);
        Assert.Equal(X509ChainTrustMode.CustomRootTrust, tls.CertificateChainPolicy!.TrustMode);
        Assert.Single(tls.CertificateChainPolicy.CustomTrustStore);
    }

    [Theory]
    [InlineData("redis://localhost:6379")]
    [InlineData("localhost:6379,password=secret")]
    [InlineData("localhost")]
    [InlineData("localhost:not-a-port")]
    public void Endpoint_accepts_only_one_host_and_port(string endpoint)
    {
        var settings = CompleteSettings();
        settings["AccountAccessGate:Endpoint"] = endpoint;

        var exception = Assert.Throws<InvalidOperationException>(
            () => AccountAccessGateOptions.FromConfiguration(Configuration(settings)));

        Assert.Contains("host:port", exception.Message, StringComparison.Ordinal);
    }

    private Dictionary<string, string?> CompleteSettings() => new()
    {
        ["AccountAccessGate:Mode"] = "enforce",
        ["AccountAccessGate:Endpoint"] = "localhost:6379",
        ["AccountAccessGate:Username"] = "forms-reader",
        ["AccountAccessGate:Password"] = "secret",
        ["AccountAccessGate:Database"] = "4",
        ["AccountAccessGate:Tls"] = "true",
        ["AccountAccessGate:CaCertificateFile"] = caCertificateFile,
        ["AccountAccessGate:ClientCertificateFile"] = clientCertificateFile,
        ["AccountAccessGate:ClientKeyFile"] = clientKeyFile
    };

    private static void WriteTlsMaterial(
        string caPath,
        string certificatePath,
        string keyPath)
    {
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest(
            "CN=SKY LAB test access gate CA",
            caKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
        using var caCertificate = caRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddDays(1));

        using var clientKey = RSA.Create(2048);
        var clientRequest = new CertificateRequest(
            "CN=forms-account-access-gate",
            clientKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        clientRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        clientRequest.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        clientRequest.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.2")],
                true));
        using var clientCertificate = clientRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddHours(1));
        using var exportedClientKey = clientCertificate.GetRSAPrivateKey();

        File.WriteAllText(caPath, caCertificate.ExportCertificatePem());
        File.WriteAllText(certificatePath, clientCertificate.ExportCertificatePem());
        File.WriteAllText(keyPath, exportedClientKey!.ExportPkcs8PrivateKeyPem());
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
