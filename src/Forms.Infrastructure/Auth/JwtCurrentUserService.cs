using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class JwtCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var identity = GetAuthenticatedIdentity();
        if (identity is null) return Task.FromResult<Guid?>(null);

        if (Guid.TryParse(identity.FindFirst("sub")?.Value, out var userId))
            return Task.FromResult<Guid?>(userId);

        return Task.FromResult<Guid?>(null);
    }

    public Task<bool> HasRoleAsync(string role, string? client = null, CancellationToken cancellationToken = default)
    {
        var identity = GetAuthenticatedIdentity();
        if (identity is null) return Task.FromResult(false);

        try
        {
            string? claimValue;

            if (client == null)
            {
                claimValue = identity.FindFirst("realm_access")?.Value;
            }
            else
            {
                var resourceAccess = identity.FindFirst("resource_access")?.Value;
                if (string.IsNullOrEmpty(resourceAccess)) return Task.FromResult(false);

                using var resourceDoc = JsonDocument.Parse(resourceAccess);
                string[] clients = client == "forms" ? ["forms", "dotnet"] : [client];
                foreach (var id in clients)
                {
                    if (!resourceDoc.RootElement.TryGetProperty(id, out var clientElement))
                        continue;
                    if (!clientElement.TryGetProperty("roles", out var clientRoles))
                        continue;
                    foreach (var r in clientRoles.EnumerateArray())
                    {
                        if (string.Equals(r.GetString(), role, StringComparison.OrdinalIgnoreCase))
                            return Task.FromResult(true);
                    }
                }
                return Task.FromResult(false);
            }

            if (string.IsNullOrEmpty(claimValue)) return Task.FromResult(false);

            using var doc = JsonDocument.Parse(claimValue);
            if (!doc.RootElement.TryGetProperty("roles", out var roles))
                return Task.FromResult(false);

            foreach (var r in roles.EnumerateArray())
            {
                if (string.Equals(r.GetString(), role, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private ClaimsIdentity? GetAuthenticatedIdentity()
    {
        return httpContextAccessor.HttpContext?.User.Identities
            .FirstOrDefault(identity => identity.IsAuthenticated);
    }
}
