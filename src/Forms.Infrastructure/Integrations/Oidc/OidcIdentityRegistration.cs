using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

public static class OidcIdentityRegistration
{
    public static IServiceCollection AddOidcIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProviderRoleOptions>(configuration.GetSection(ProviderRoleOptions.SectionName));
        services.AddTransient<IClaimsTransformation, ProviderRoleClaims>();

        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));
        services.AddHttpClient("keycloak");
        services.AddSingleton<IServiceTokenProvider, KeycloakServiceTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();

        return services;
    }
}
