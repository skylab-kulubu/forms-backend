using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Skylab.Forms.Infrastructure.Integrations.Cloudflare;

internal sealed class TurnstileOptions
{
    public const string SectionName = "Integrations:Turnstile";

    private static readonly HashSet<string> TestSecrets = new(StringComparer.Ordinal)
    {
        "1x0000000000000000000000000000000AA",
        "2x0000000000000000000000000000000AA",
        "3x0000000000000000000000000000000AA"
    };

    public string SecretKey { get; init; } = "";

    public string AllowedHostnames { get; init; } = "";

    public bool AllowTestSecret { get; init; }

    public string Secret => SecretKey.Trim();

    public bool UsesTestSecret => IsTestSecret(SecretKey);

    public IReadOnlySet<string> Hostnames =>
        AllowedHostnames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsTestSecret(string? secret) => secret is not null && TestSecrets.Contains(secret.Trim());
}

internal sealed class TurnstileOptionsValidator(IHostEnvironment environment) : IValidateOptions<TurnstileOptions>
{
    public ValidateOptionsResult Validate(string? name, TurnstileOptions options)
    {
        if (options.Hostnames.Any(hostname => Uri.CheckHostName(hostname) is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6)))
            return ValidateOptionsResult.Fail("Integrations:Turnstile:AllowedHostnames must be a comma-separated list of host names, without scheme or port.");

        if (options.UsesTestSecret && !options.AllowTestSecret && !environment.IsDevelopment())
            return ValidateOptionsResult.Fail("Integrations:Turnstile:SecretKey is a Cloudflare test key, which passes every token. Use it only in Development, or set Integrations:Turnstile:AllowTestSecret=true.");

        return ValidateOptionsResult.Success;
    }
}
