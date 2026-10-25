using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Skylab.Forms.Infrastructure.Auth;

/// <summary>
/// Doğrulanmış token'daki sağlayıcı rollerini Forms izinlerine çevirir. Keycloak'ın resource_access biçimini bilen tek
/// yer burasıdır; başka bir sağlayıcıya geçmek bu sınıfı değiştirmek demektir.
/// </summary>
public sealed class ProviderRoleClaims(IOptions<ProviderRoleOptions> options) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var roles = options.Value;

        foreach (var identity in principal.Identities.Where(identity => identity.IsAuthenticated))
        {
            // Dönüşüm bir istekte birden çok kez çalışabilir; aynı izin iki kez eklenmesin.
            Grant(identity, roles, roles.PlatformAdmin, FormsPermissions.PlatformAdmin);
            Grant(identity, roles, roles.AccountErasure, FormsPermissions.AccountErasure);
        }

        return Task.FromResult(principal);
    }

    private static void Grant(ClaimsIdentity identity, ProviderRoleOptions roles, string role, string permission)
    {
        if (string.IsNullOrEmpty(role) || identity.HasClaim(claim => claim.Type == FormsPermissions.ClaimType && claim.Value == permission && claim.Issuer == FormsPermissions.Issuer))
            return;

        if (HasClientRole(identity, roles.Clients, role))
            identity.AddClaim(new Claim(FormsPermissions.ClaimType, permission, ClaimValueTypes.String, FormsPermissions.Issuer));
    }

    private static bool HasClientRole(ClaimsIdentity identity, IReadOnlyCollection<string> clients, string role)
    {
        var claim = identity.FindFirst("resource_access")?.Value;
        if (string.IsNullOrEmpty(claim)) return false;

        try
        {
            using var document = JsonDocument.Parse(claim);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;

            // Beklenmeyen bir biçim izin vermez; dönüşümün fırlatması o token'la gelen her isteği 500'e düşürürdü.
            return clients.Any(client =>
                document.RootElement.TryGetProperty(client, out var access)
                && access.ValueKind == JsonValueKind.Object
                && access.TryGetProperty("roles", out var granted)
                && granted.ValueKind == JsonValueKind.Array
                && granted.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && string.Equals(value.GetString(), role, StringComparison.OrdinalIgnoreCase)));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
