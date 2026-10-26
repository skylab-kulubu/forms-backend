using Skylab.Forms.Api.Auth;
using Skylab.Forms.Application.Services;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.Contracts.ShortLinks;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Ownership;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Application.Services.Ownership;
using Skylab.Forms.Application.Services.Forms;
using Skylab.Forms.Application.Services.Responses;
using Skylab.Forms.Application.Services.ShortLinks;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Api.Endpoints;

public static class FormAdminEndpoints
{
    private const string FormRoute = "AdminForm";
    private const string ComponentGroupRoute = "AdminComponentGroup";
    internal const string ResponseRoute = "AdminResponse";

    public static void MapFormAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/forms/").WithTags("FormsAdmin").RequireAuthorization();

        group.MapGet("/", async (IFormCatalog service, ICurrentUser user, [AsParameters] GetUserFormsRequest request, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetUserFormsAsync(userId, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/all", async (IFormCatalog service, [AsParameters] GetAllFormsRequest request, CancellationToken ct) =>
        {
            var result = await service.GetAllFormsAsync(request, ct);
            return result.ToApiResult();
        }).RequireAuthorization(FormsAuthorization.PlatformAdmin);

        // Hesabı silinen kişilerden kalan, sahibi Silinmiş kullanıcı olan formlar.
        group.MapGet("/orphaned", async (IFormCatalog service, [AsParameters] GetUserFormsRequest request, CancellationToken ct) =>
        {
            var result = await service.GetUserFormsAsync(DeletedUser.Id, request with { Role = CollaboratorRole.Owner }, ct);
            return result.ToApiResult();
        }).RequireAuthorization(FormsAuthorization.PlatformAdmin);

        group.MapGet("/metrics", async (IFormMetricService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetServiceMetricsAsync(userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormCatalog service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetFormByIdAsync(id, userId, ct);
            return result.ToApiResult();
        }).WithName(FormRoute);

        group.MapGet("/{id:guid}/info", async (Guid id, IFormCatalog service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetFormInfoByIdAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] FormUpsertRequest request, IFormAuthoring service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CreateFormAsync(request, userId, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return result.ToCreatedResult(httpContext, FormRoute, new { id = result.Data.Id });
            }

            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] FormUpsertRequest request, IFormAuthoring service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateFormAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/draft", async (Guid id, IFormDraftService draftService, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await draftService.GetFormDraftAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/draft", async (Guid id, [FromBody] FormDraftRequest request, IFormDraftService draftService, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            if (id != request.FormId) return ServiceStatus.NotAcceptable.ToApiResult("Rota ID'si ile taslak ID'si uyuşmuyor.");

            var result = await draftService.SaveFormDraftAsync(id, userId, request, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}/draft", async (Guid id, IFormDraftService draftService, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await draftService.DeleteFormDraftAsync(id, userId, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormAuthoring service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.DeleteFormAsync(id, userId, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapPost("/{id:guid}/transfer", async (Guid id, [FromBody] OwnershipTransferRequest request, IOwnershipService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.TransferFormAsync(id, request.UserId, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses", async (Guid id, [AsParameters] GetResponsesRequest request, IResponseQueries service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetFormResponsesAsync(id, userId, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses/{responseId:guid}/page", async (Guid id, Guid responseId, [AsParameters] GetResponsesRequest request, IResponseQueries service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetResponsePageAsync(id, responseId, userId, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/mail-recipients", async (Guid id, IResponseExports service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";

            var userId = user.SignedInId();

            var result = await service.GetFormMailRecipientsAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/mail-source", async (Guid id, IResponseExports service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";

            var userId = user.SignedInId();

            var result = await service.PrepareFormMailSourceAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/metrics", async (Guid id, IFormMetricService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetFormMetricsAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/analytics", async (Guid id, IFormMetricService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetAnswerAnalyticsAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/short-link", async (Guid id, IFormShortLinkService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/short-link", async (Guid id, IFormShortLinkService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.EnsureAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPatch("/{id:guid}/short-link", async (Guid id, [FromBody] ShortLinkRenameRequest request, IFormShortLinkService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.RenameAsync(id, userId, request.Alias, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/short-link/availability", async (Guid id, [FromQuery] string alias, IFormShortLinkService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CheckAliasAsync(id, userId, alias, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/short-link/qr", async (Guid id, [FromQuery] string? format, [FromQuery] string? source, [FromQuery] string? campaign, [FromQuery] string? content, IFormShortLinkService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var svg = string.Equals(format, "svg", StringComparison.OrdinalIgnoreCase);
            var result = await service.GetQrAsync(id, userId, svg, source, campaign, content, ct);
            if (result.Status.IsFailure() || result.Data is null) return result.ToApiResult();

            return Results.File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
        });

        group.MapGet("/responses/{id:guid}", async (Guid id, [FromQuery] string? token, IResponseQueries service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetResponseByIdAsync(id, userId, token, ct);
            return result.ToApiResult();
        }).WithName(ResponseRoute);

        group.MapGet("/responses/{responseId:guid}/files/{mediaId:guid}", async (Guid responseId, Guid mediaId, [FromQuery] string? token, IResponseFiles service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetResponseFileAsync(responseId, mediaId, userId, token, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{responseId:guid}/files/{mediaId:guid}/link", async (Guid responseId, Guid mediaId, [FromQuery] string? token, IResponseFiles service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";

            var userId = user.SignedInId();

            var result = await service.CreateResponseFileLinkAsync(responseId, mediaId, userId, token, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{id:guid}/share", async (Guid id, IResponseShareLinks service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CreateOrRefreshShareTokenAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{id:guid}/revoke-token", async (Guid id, IResponseShareLinks service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.RevokeShareTokenAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPatch("/responses/{id:guid}/status", async (Guid id, [FromBody] ResponseStatusUpdateRequest request, IResponseReview service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var serviceContract = new ResponseStatusUpdateRequest(id, request.NewStatus, request.Note, request.QuestionIds);

            var result = await service.UpdateResponseStatusAsync(serviceContract, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{id:guid}/archive", async (Guid id, IResponseReview service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.ArchiveResponseAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/attempts/{id:guid}", async (Guid id, IAttemptReadModels service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetViewAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/attempts/{id:guid}/extend", async (Guid id, [FromBody] AttemptExtendRequest request, IAttemptDecisions service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.ExtendAsync(id, userId, request, ct);
            return result.ToApiResult();
        });

        group.MapPost("/attempts/{id:guid}/accept", async (Guid id, [FromBody] AttemptDecisionRequest? request, IAttemptDecisions service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.AcceptAsync(id, userId, request, ct);
            return result.ToApiResult();
        });

        group.MapPost("/attempts/{id:guid}/close", async (Guid id, [FromBody] AttemptDecisionRequest? request, IAttemptDecisions service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CloseAsync(id, userId, request, ct);
            return result.ToApiResult();
        });

        group.MapPost("/attempts/{id:guid}/remind", async (Guid id, IAttemptDecisions service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.RemindAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/attempts/analytics", async (Guid id, IAttemptReadModels service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetAnalyticsAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses/export", async (Guid id, IResponseExports service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.ExportResponsesToExcelAsync(id, userId, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return Results.File(
                    fileContents: result.Data,
                    contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    fileDownloadName: $"FormCevaplari_{id.ToString().Substring(0, 8)}.xlsx"
                );
            }

            return result.ToApiResult();
        });

        group.MapGet("/component-groups", async (IComponentGroupService service, ICurrentUser user, [AsParameters] GetComponentGroupsRequest request, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetUserGroupsAsync(userId, request, ct);
            return result.ToApiResult();
        });

        // Hesabı silinen kişilerden kalan, sahibi Silinmiş kullanıcı olan şablonlar.
        group.MapGet("/component-groups/orphaned", async (IComponentGroupService service, [AsParameters] GetComponentGroupsRequest request, CancellationToken ct) =>
        {
            var result = await service.GetUserGroupsAsync(DeletedUser.Id, request, ct);
            return result.ToApiResult();
        }).RequireAuthorization(FormsAuthorization.PlatformAdmin);

        group.MapGet("/component-groups/{id:guid}", async (Guid id, [FromQuery] string? token, IComponentGroupService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetGroupByIdAsync(id, userId, token, ct);
            return result.ToApiResult();
        }).WithName(ComponentGroupRoute);

        group.MapPost("/component-groups/{id:guid}/share", async (Guid id, IComponentGroupService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CreateOrRefreshShareTokenAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/component-groups/{id:guid}/clone", async (Guid id, [FromQuery] string token, IComponentGroupService service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CloneGroupAsync(id, userId, token, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
                return result.ToCreatedResult(httpContext, ComponentGroupRoute, new { id = result.Data.Id });

            return result.ToApiResult();
        });

        group.MapPost("/component-groups", async ([FromBody] ComponentGroupUpsertRequest request, IComponentGroupService service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CreateGroupAsync(request, userId, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return result.ToCreatedResult(httpContext, ComponentGroupRoute, new { id = result.Data.Id });
            }

            return result.ToApiResult();
        });

        group.MapPut("/component-groups/{id:guid}", async (Guid id, [FromBody] ComponentGroupUpsertRequest request, IComponentGroupService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateGroupAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/component-groups/{id:guid}", async (Guid id, IComponentGroupService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.DeleteGroupAsync(id, userId, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapPost("/component-groups/{id:guid}/transfer", async (Guid id, [FromBody] OwnershipTransferRequest request, IOwnershipService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.TransferTemplateAsync(id, request.UserId, userId, ct);
            return result.ToApiResult();
        });
    }
}
