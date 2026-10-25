namespace Skylab.Forms.Infrastructure.Auth;

public sealed class ProviderRoleOptions
{
    public const string SectionName = "Authentication:Roles";

    public string[] Clients { get; set; } = [];

    public string PlatformAdmin { get; set; } = "";

    public string AccountErasure { get; set; } = "";
}
