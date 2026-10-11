using Microsoft.Extensions.Configuration;

namespace Skylab.Forms.Infrastructure.Turnstile;

public sealed class TurnstileOptions
{
    private static readonly HashSet<string> TestSecrets = new(StringComparer.Ordinal)
    {
        "1x0000000000000000000000000000000AA",
        "2x0000000000000000000000000000000AA",
        "3x0000000000000000000000000000000AA"
    };

    public string? SecretKey { get; private init; }
    public IReadOnlySet<string> AllowedHostnames { get; private init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled => SecretKey is not null;
    public bool UsesTestSecret => SecretKey is not null && TestSecrets.Contains(SecretKey);

    public static TurnstileOptions FromConfiguration(IConfiguration configuration)
    {
        var secret = Read(configuration, "TURNSTILE_SECRET_KEY", "SecretKey")?.Trim();
        var hostnames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var configuredHostnames = (Read(configuration, "TURNSTILE_ALLOWED_HOSTNAMES", "AllowedHostnames") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var hostname in configuredHostnames)
        {
            if (Uri.CheckHostName(hostname) is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
            {
                throw new InvalidOperationException(
                    "TURNSTILE_ALLOWED_HOSTNAMES must be a comma-separated list of host names, without scheme or port.");
            }

            hostnames.Add(hostname);
        }

        var options = new TurnstileOptions
        {
            SecretKey = string.IsNullOrEmpty(secret) ? null : secret,
            AllowedHostnames = hostnames
        };

        var testSecretAllowed = string.Equals(Read(configuration, "TURNSTILE_ALLOW_TEST_SECRET", "AllowTestSecret")?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        if (options.UsesTestSecret && !testSecretAllowed && !IsDevelopment(configuration))
        {
            throw new InvalidOperationException(
                "TURNSTILE_SECRET_KEY is a Cloudflare test key, which passes every token. Use it only in Development, or set TURNSTILE_ALLOW_TEST_SECRET=true.");
        }

        return options;
    }

    private static bool IsDevelopment(IConfiguration configuration)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? configuration["environment"];

        return string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Read(IConfiguration configuration, string environmentName, string optionName)
    {
        return Environment.GetEnvironmentVariable(environmentName)
            ?? configuration[$"Turnstile:{optionName}"];
    }
}
