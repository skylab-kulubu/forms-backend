using System.Security.Claims;

namespace Skylab.Forms.Infrastructure.Auth;

public static class FormsPermissions
{
    public const string ClaimType = "forms:permission";
    public const string PlatformAdmin = "platform-admin";
    public const string AccountErasure = "account-erasure";

    internal const string Issuer = "forms";

    public static bool Has(ClaimsPrincipal? principal, string permission) =>
        principal?.HasClaim(claim => claim.Type == ClaimType && claim.Value == permission && claim.Issuer == Issuer) == true;
}
