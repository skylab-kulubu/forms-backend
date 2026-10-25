using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Integrations.Cloudflare;

public static class TurnstileRegistration
{
    /// <summary>Girişsiz isteklerin bir insandan geldiğini Cloudflare Turnstile ile doğrular.</summary>
    public static IServiceCollection AddTurnstile(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TurnstileOptions.FromConfiguration(configuration));
        services.AddSingleton<TurnstileReachability>();

        services.AddHttpClient<IHumanVerifier, CloudflareTurnstileVerifier>(client =>
        {
            client.BaseAddress = new Uri("https://challenges.cloudflare.com");
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }
}
