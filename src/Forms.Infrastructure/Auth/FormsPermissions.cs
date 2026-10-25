using System.Security.Claims;

namespace Skylab.Forms.Infrastructure.Auth;

/// <summary>Kimlik sağlayıcının rollerinden çevrilen Forms izinleri; uçlar ve servisler yalnız bunları okur.</summary>
public static class FormsPermissions
{
    public const string ClaimType = "forms:permission";
    public const string PlatformAdmin = "platform-admin";
    public const string AccountErasure = "account-erasure";

    // Token aynı türde bir claim taşısa bile sayılmaz: yalnız ProviderRoleClaims'in eklediği izin geçerlidir.
    internal const string Issuer = "forms";

    public static bool Has(ClaimsPrincipal? principal, string permission) =>
        principal?.HasClaim(claim => claim.Type == ClaimType && claim.Value == permission && claim.Issuer == Issuer) == true;
}
