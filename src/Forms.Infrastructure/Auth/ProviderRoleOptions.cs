namespace Skylab.Forms.Infrastructure.Auth;

/// <summary>Kimlik sağlayıcıda hangi rolün hangi Forms iznine karşılık geldiği. Boş bırakılan izni kimse almaz.</summary>
public sealed class ProviderRoleOptions
{
    public const string SectionName = "Authentication:Roles";

    /// <summary>Rollerin arandığı istemciler (Keycloak resource_access altında).</summary>
    public string[] Clients { get; set; } = [];

    public string PlatformAdmin { get; set; } = "";

    public string AccountErasure { get; set; } = "";
}
