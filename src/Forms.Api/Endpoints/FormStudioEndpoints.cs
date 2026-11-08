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

        group.MapGet("/{id:guid}/comments", async (Guid id, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.ListAsync(id, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/comments", async (Guid id, [FromBody] StudioThreadRequest request, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.StartAsync(id, request, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/comments/{threadId:guid}/replies", async (Guid id, Guid threadId, [FromBody] StudioCommentRequest request, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.ReplyAsync(id, threadId, request, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/comments/{commentId:guid}", async (Guid id, Guid commentId, [FromBody] StudioCommentRequest request, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.EditAsync(id, commentId, request, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}/comments/{commentId:guid}", async (Guid id, Guid commentId, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.DeleteAsync(id, commentId, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/comments/{threadId:guid}/resolve", async (Guid id, Guid threadId, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.ResolveAsync(id, threadId, true, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/comments/{threadId:guid}/reopen", async (Guid id, Guid threadId, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.ResolveAsync(id, threadId, false, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/comments/reads", async (Guid id, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.ReadsAsync(id, CallerOf(user), ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/comments/{threadId:guid}/read", async (Guid id, Guid threadId, [FromBody] StudioReadRequest request, IFormComments comments, ICurrentUser user, CancellationToken ct) =>
        {
            var result = await comments.MarkReadAsync(id, threadId, request, CallerOf(user), ct);
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
