using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class JwtCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Guid.TryParse(Identity?.FindFirst("sub")?.Value, out var userId) ? userId : (Guid?)null);

    public Task<bool> HasRoleAsync(string role, string? client = null, CancellationToken cancellationToken = default)
    {
        var claim = Identity?.FindFirst(client == null ? "realm_access" : "resource_access")?.Value;
        if (string.IsNullOrEmpty(claim)) return Task.FromResult(false);

        try
        {
            using var doc = JsonDocument.Parse(claim);

            if (client == null) return Task.FromResult(HasRole(doc.RootElement, role));

            // Forms'un rolleri Keycloak'taki eski "dotnet" istemcisinde de durabilir.
            string[] clients = client == "forms" ? ["forms", "dotnet"] : [client];

            return Task.FromResult(clients.Any(id => doc.RootElement.TryGetProperty(id, out var access) && HasRole(access, role)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    private ClaimsIdentity? Identity =>
        httpContextAccessor.HttpContext?.User.Identities.FirstOrDefault(identity => identity.IsAuthenticated);

    private static bool HasRole(JsonElement access, string role) =>
        access.TryGetProperty("roles", out var roles)
        && roles.EnumerateArray().Any(r => string.Equals(r.GetString(), role, StringComparison.OrdinalIgnoreCase));
}
