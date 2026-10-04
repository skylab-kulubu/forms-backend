using Skylab.Forms.Application.Services;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.Services.Attempts;

namespace Skylab.Forms.Api.Endpoints;

public static class FormEndpoints
{
    public static void MapFormEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/forms").WithTags("Forms");

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            var result = await service.GetDisplayFormByIdAsync(id, userId, ct);

            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/meta", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var result = await service.GetFormMetaByIdAsync(id, ct);

            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/attempt", async (Guid id, IFormAttemptService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Görevi başlatmak için giriş yapmalısınız.");

            var result = await service.StartAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses", async ([FromBody] ResponseSubmitRequest request, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            var result = await service.SubmitResponseAsync(request, userId, ct);

            if (result.Status == ServiceStatus.Success || result.Status == ServiceStatus.PendingApproval)
                return Results.Created($"/api/forms/responses/{result.Data?.ResponseId}", result);

            return result.ToApiResult();
        });

        group.MapPost("/responses/draft", async ([FromBody] ResponseDraftRequest request, IFormDraftService draftService, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult();

            var result = await draftService.SaveResponseDraftAsync(request.FormId, userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/responses/draft/{formId:guid}", async (Guid formId, IFormDraftService draftService, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult();

            var result = await draftService.GetResponseDraftAsync(formId, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/responses/draft/{formId:guid}", async (Guid formId, IFormDraftService draftService, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult();

            var result = await draftService.DeleteResponseDraftAsync(formId, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapGet("/component-groups/{id:guid}/meta", async (Guid id, [FromQuery] string token, IComponentGroupService service, CancellationToken ct) =>
        {
            var result = await service.GetGroupMetaAsync(id, token, ct);
            return result.ToApiResult();
        });

        group.MapGet("/responses/{id:guid}/meta", async (Guid id, [FromQuery] string token, IFormResponseService service, CancellationToken ct) =>
        {
            var result = await service.GetResponseMetaAsync(id, token, ct);
            return result.ToApiResult();
        });
    }
}
