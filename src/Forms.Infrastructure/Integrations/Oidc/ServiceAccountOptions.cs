using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

/// <summary>Forms'un diğer servislere client credentials ile aldığı token'ın hesabı.</summary>
internal sealed class ServiceAccountOptions
{
    public const string SectionName = "Authentication:ServiceAccount";

    public string TokenUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scope { get; set; } = "openid";
}

internal static class ServiceAccountRequirement
{
    /// <summary>Servis hesabıyla giden bir sağlayıcı açıkken hesabın ayarları açılışta doğrulanır; iki sağlayıcı bir kez sayılır.</summary>
    public static void RequireServiceAccount(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<ServiceAccountOptions>, ServiceAccountValidator>());
        services.AddOptions<ServiceAccountOptions>().ValidateOnStart();
    }

    private sealed class ServiceAccountValidator : IValidateOptions<ServiceAccountOptions>
    {
        public ValidateOptionsResult Validate(string? name, ServiceAccountOptions account) =>
            HttpUrl.IsValid(account.TokenUrl) && account.ClientId.Length > 0 && account.ClientSecret.Length > 0
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("Authentication:ServiceAccount:TokenUrl, ClientId and ClientSecret must be set while core or SkyMail is configured.");
    }
}
