using Skylab.Forms.Application.Services;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Draft;

namespace Skylab.Forms.Api.Endpoints;

public static class FormAdminEndpoints
{
    public static void MapFormAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/forms/").WithTags("FormsAdmin");

        group.MapGet("/", async (IFormService service, ICurrentUserService userService, [AsParameters] GetUserFormsRequest request, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Formları görmek için giriş yapmalısınız.");

            var result = await service.GetUserFormsAsync(userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/all", async (IFormService service, ICurrentUserService userService, [AsParameters] GetAllFormsRequest request, CancellationToken ct) =>
        {
            if (!await userService.HasRoleAsync("skyforms:*", "forms", ct))
                return ServiceStatus.NotAuthorized.ToApiResult();

            var result = await service.GetAllFormsAsync(request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/metrics", async (IFormMetricService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Metrikleri görmek için giriş yapmalısınız.");

            var result = await service.GetServiceMetricsAsync(userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Formu görmek için giriş yapmalısınız.");

            var result = await service.GetFormByIdAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/info", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Formu görmek için giriş yapmalısınız.");

            var result = await service.GetFormInfoByIdAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] FormUpsertRequest request, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Form oluşturmak için giriş yapmalısınız.");

            var result = await service.CreateFormAsync(request, userId.Value, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return Results.Created($"/api/admin/forms/{result.Data.Id}", result);
            }

            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] FormUpsertRequest request, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Form güncellemek için giriş yapmalısınız.");

            var result = await service.UpdateFormAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/draft", async (Guid id, IFormDraftService draftService, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult();

            var result = await draftService.GetFormDraftAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/draft", async (Guid id, [FromBody] FormDraftRequest request, IFormDraftService draftService, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult();

            if (id != request.FormId) return ServiceStatus.NotAcceptable.ToApiResult("Rota ID'si ile taslak ID'si uyuşmuyor.");

            var result = await draftService.SaveFormDraftAsync(id, userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}/draft", async (Guid id, IFormDraftService draftService, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult();

            var result = await draftService.DeleteFormDraftAsync(id, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Form silmek için giriş yapmalısınız.");

            var result = await service.DeleteFormAsync(id, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses", async (Guid id, [AsParameters] GetResponsesRequest request, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Cevapları görmek için giriş yapmalısınız.");

            var result = await service.GetFormResponsesAsync(id, userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/metrics", async (Guid id, IFormMetricService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Form metriklerini görmek için giriş yapmalısınız.");

            var result = await service.GetFormMetricsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/analytics", async (Guid id, IFormMetricService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Analitiği görmek için giriş yapmalısınız.");

            var result = await service.GetAnswerAnalyticsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/responses/{id:guid}", async (Guid id, [FromQuery] string? token, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Cevabı görmek için giriş yapmalısınız.");

            var result = await service.GetResponseByIdAsync(id, userId.Value, token, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{id:guid}/share", async (Guid id, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Cevap paylaşmak için giriş yapmalısınız.");

            var result = await service.CreateOrRefreshShareTokenAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{id:guid}/revoke-token", async (Guid id, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Paylaşımı iptal etmek için giriş yapmalısınız.");

            var result = await service.RevokeShareTokenAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPatch("/responses/{id:guid}/status", async (Guid id, [FromBody] ResponseStatusUpdateRequest request, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Durumu güncellemek için giriş yapmalısınız.");

            var serviceContract = new ResponseStatusUpdateRequest(id, request.NewStatus, request.Note);

            var result = await service.UpdateResponseStatusAsync(serviceContract, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/responses/{id:guid}/archive", async (Guid id, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Cevabı arşivlemek için giriş yapmalısınız.");

            var result = await service.ArchiveResponseAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses/export", async (Guid id, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Excel indirmek için giriş yapmalısınız.");

            var result = await service.ExportResponsesToExcelAsync(id, userId.Value, ct);

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

        group.MapGet("/component-groups", async (IComponentGroupService service, ICurrentUserService userService, [AsParameters] GetComponentGroupsRequest request, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablonları görmek için giriş yapmalısınız.");

            var result = await service.GetUserGroupsAsync(userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/component-groups/{id:guid}", async (Guid id, [FromQuery] string? token, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablonu görmek için giriş yapmalısınız.");

            var result = await service.GetGroupByIdAsync(id, userId.Value, token, ct);
            return result.ToApiResult();
        });

        group.MapPost("/component-groups/{id:guid}/share", async (Guid id, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablon paylaşmak için giriş yapmalısınız.");

            var result = await service.CreateOrRefreshShareTokenAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/component-groups/{id:guid}/clone", async (Guid id, [FromQuery] string token, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablon eklemek için giriş yapmalısınız.");

            var result = await service.CloneGroupAsync(id, userId.Value, token, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
                return Results.Created($"/api/admin/component-groups/{result.Data.Id}", result);

            return result.ToApiResult();
        });

        group.MapPost("/component-groups", async ([FromBody] ComponentGroupUpsertRequest request, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablon oluşturmak için giriş yapmalısınız.");

            var result = await service.CreateGroupAsync(request, userId.Value, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
            {
                return Results.Created($"/component-groups/api/admin/component-groups/{result.Data.Id}", result);
            }

            return result.ToApiResult();
        });

        group.MapPut("/component-groups/{id:guid}", async (Guid id, [FromBody] ComponentGroupUpsertRequest request, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablon güncellemek için giriş yapmalısınız.");

            var result = await service.UpdateGroupAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/component-groups/{id:guid}", async (Guid id, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablonu arşivlemek için giriş yapmalısınız.");

            var result = await service.DeleteGroupAsync(id, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapPost("/component-groups/{id:guid}/restore", async (Guid id, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Şablonu geri yüklemek için giriş yapmalısınız.");

            var result = await service.RestoreGroupAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

    }
}
