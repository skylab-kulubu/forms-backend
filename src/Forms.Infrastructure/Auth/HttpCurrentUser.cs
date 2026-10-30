using Microsoft.AspNetCore.Http;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? Id =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) ? id : null;

    public bool IsPlatformAdmin => FormsPermissions.Has(httpContextAccessor.HttpContext?.User, FormsPermissions.PlatformAdmin);
}
