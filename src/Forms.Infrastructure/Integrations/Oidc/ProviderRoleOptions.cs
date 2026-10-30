namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

internal sealed class ProviderRoleOptions
{
    public const string SectionName = "Authentication:Roles";

    public string[] Clients { get; set; } = [];

    public string PlatformAdmin { get; set; } = "";

    public string AccountErasure { get; set; } = "";
}
