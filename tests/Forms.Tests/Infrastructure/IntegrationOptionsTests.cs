using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Skylab.Forms.Infrastructure.Integrations.Cloudflare;
using Skylab.Forms.Infrastructure.Integrations.SkylabCore;

namespace Skylab.Forms.Tests.Infrastructure;

public class IntegrationOptionsTests
{
    private static readonly AccountAccessGateOptionsValidator Gate = new();

    private static TurnstileOptionsValidator Turnstile(string environment) => new(new HostingEnvironment { EnvironmentName = environment });

    [Fact]
    public void A_gate_that_is_off_needs_nothing_else() =>
        Assert.True(Gate.Validate(null, new AccountAccessGateOptions { Mode = "off" }).Succeeded);

    [Fact]
    public void The_empty_values_compose_passes_bind_as_unset()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AccountAccessGate:Mode"] = "off",
            ["AccountAccessGate:Endpoint"] = "",
            ["AccountAccessGate:Database"] = "",
            ["AccountAccessGate:Tls"] = "false",
            ["AccountAccessGate:OperationTimeoutMilliseconds"] = "200"
        }).Build();

        var options = configuration.GetSection(AccountAccessGateOptions.SectionName).Get<AccountAccessGateOptions>()!;

        Assert.Equal((null, false, 200), (options.DatabaseNumber, options.UsesTls, options.OperationTimeoutMilliseconds));
        Assert.True(Gate.Validate(null, options).Succeeded);
    }

    [Fact]
    public void A_gate_that_is_off_ignores_leftover_redis_settings() =>
        Assert.True(Gate.Validate(null, new AccountAccessGateOptions { Mode = "off", Database = "abc", Tls = "1" }).Succeeded);

    [Fact]
    public void An_enforcing_gate_reads_its_database_and_tls_strictly()
    {
        var result = Gate.Validate(null, new AccountAccessGateOptions { Mode = "enforce", Endpoint = "gate:6379", Username = "forms", Password = "secret", Database = "abc", Tls = "1" });

        Assert.Contains("AccountAccessGate:Database must be a database number", result.FailureMessage);
        Assert.Contains("AccountAccessGate:Tls must be 'true' or 'false'", result.FailureMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("on")]
    public void The_gate_mode_is_chosen_explicitly(string? mode) =>
        Assert.True(Gate.Validate(null, new AccountAccessGateOptions { Mode = mode }).Failed);

    [Fact]
    public void An_enforcing_gate_names_every_missing_setting()
    {
        var result = Gate.Validate(null, new AccountAccessGateOptions { Mode = "enforce", Endpoint = "redis://gate:6379" });

        Assert.True(result.Failed);
        Assert.Contains("AccountAccessGate:Endpoint must contain one host:port", result.FailureMessage);
        Assert.Contains("AccountAccessGate:Username", result.FailureMessage);
        Assert.Contains("AccountAccessGate:Tls", result.FailureMessage);
    }

    [Fact]
    public void An_enforcing_gate_over_tls_needs_its_certificate_files()
    {
        var result = Gate.Validate(null, new AccountAccessGateOptions { Mode = "enforce", Endpoint = "gate:6379", Username = "forms", Password = "secret", Database = "2", Tls = "true" });

        Assert.Contains("AccountAccessGate:CaCertificateFile", result.FailureMessage);
    }

    [Theory]
    [InlineData("Production", false, false)]
    [InlineData("Development", false, true)]
    [InlineData("Production", true, true)]
    public void A_test_secret_runs_only_in_development_or_when_allowed(string environment, bool allowTestSecret, bool succeeds)
    {
        var options = new TurnstileOptions { SecretKey = "1x0000000000000000000000000000000AA", AllowTestSecret = allowTestSecret };

        Assert.Equal(succeeds, Turnstile(environment).Validate(null, options).Succeeded);
    }

    [Fact]
    public void An_allowed_hostname_has_no_scheme() =>
        Assert.True(Turnstile(Environments.Production).Validate(null, new TurnstileOptions { SecretKey = "secret", AllowedHostnames = "forms.example.org, https://forms.example.org" }).Failed);
}
