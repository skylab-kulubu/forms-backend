namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

internal class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    public string TokenUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "openid";
}
