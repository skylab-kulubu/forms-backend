using Microsoft.AspNetCore.Mvc;
using Skylab.Forms.Api.Auth;
using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Application.Services.Forms;
using Skylab.Forms.Application.Services.Studio;

namespace Skylab.Forms.Api.Endpoints;

public static class FormStudioEndpoints
{
    public static void MapFormStudioEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/forms/").WithTags("FormStudio").RequireAuthorization();

        group.MapPost("/studio", async ([FromBody] StudioStartRequest request, IFormStudio studio, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            var result = await studio.StartAsync(request, CallerOf(user), ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return result.ToCreatedResult(httpContext, FormAdminEndpoints.FormRoute, new { id = result.Data.Form.Id });
            }

            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/studio", async (Guid id, IFormStudio studio, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await studio.GetAsync(id, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/studio/publish", async (Guid id, [FromBody] StudioPublishRequest request, IFormStudio studio, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await studio.PublishAsync(id, request, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/collaborators", async (Guid id, [FromBody] FormCollaboratorsRequest request, IFormAuthoring service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateCollaboratorsAsync(id, request, userId, ct);
            return result.ToApiResult();
        });
    }

    private static StudioCaller CallerOf(ICurrentUser user) => new(user.SignedInId(), user.IsPlatformAdmin);
}
