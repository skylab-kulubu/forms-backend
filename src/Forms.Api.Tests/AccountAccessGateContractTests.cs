using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Api.Auth;
using Skylab.Forms.Infrastructure.AccountAccess;
using StackExchange.Redis;
using Xunit;

namespace Skylab.Forms.Api.Tests;

public sealed class AccountAccessGateContractTests
{
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
    public void Enforce_mode_requires_every_connection_setting(string missingSetting)
    {
        var settings = CompleteSettings();
        settings.Remove($"AccountAccessGate:{missingSetting}");

        var exception = Assert.Throws<InvalidOperationException>(
            () => AccountAccessGateOptions.FromConfiguration(Configuration(settings)));

        Assert.Contains("required", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private static Dictionary<string, string?> CompleteSettings() => new()
    {
        ["AccountAccessGate:Mode"] = "enforce",
        ["AccountAccessGate:Endpoint"] = "localhost:6379",
        ["AccountAccessGate:Username"] = "forms-reader",
        ["AccountAccessGate:Password"] = "secret",
        ["AccountAccessGate:Database"] = "4",
        ["AccountAccessGate:Tls"] = "true"
    };

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
