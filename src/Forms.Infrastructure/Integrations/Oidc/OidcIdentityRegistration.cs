using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

public static class OidcIdentityRegistration
{
    public static IServiceCollection AddOidcIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProviderRoleOptions>().BindConfiguration(ProviderRoleOptions.SectionName);
        services.AddTransient<IClaimsTransformation, ProviderRoleClaims>();

        services.AddOptions<ServiceAccountOptions>().BindConfiguration(ServiceAccountOptions.SectionName);
        services.AddHttpClient(ServiceAccountTokenProvider.HttpClientName);
        services.AddSingleton<IServiceTokenProvider, ServiceAccountTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();

        return services;
    }
}
