using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Integrations.Cloudflare;

public static class TurnstileRegistration
{
    /// <summary>Girişsiz isteklerin bir insandan geldiğini Cloudflare Turnstile ile doğrular; gizli anahtar yoksa doğrulama kapalıdır.</summary>
    public static IServiceCollection AddTurnstile(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration[$"{TurnstileOptions.SectionName}:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            services.AddSingleton<IHumanVerifier, UnconfiguredHumanVerifier>();
            return services;
        }

        services.AddOptions<TurnstileOptions>().BindConfiguration(TurnstileOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<TurnstileOptions>, TurnstileOptionsValidator>();
        services.AddSingleton<TurnstileReachability>();

        if (TurnstileOptions.IsTestSecret(secret))
            services.AddSingleton(new IntegrationNotice(LogLevel.Warning, "Turnstile gizli anahtarı bir Cloudflare test anahtarı; hostname ve action denetlenmiyor"));

        services.AddHttpClient<IHumanVerifier, CloudflareTurnstileVerifier>(client =>
        {
            client.BaseAddress = new Uri("https://challenges.cloudflare.com");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }
}
