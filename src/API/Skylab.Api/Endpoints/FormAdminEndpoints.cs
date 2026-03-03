using Skylab.Forms.Application.Services;
using Skylab.Api.Extensions;
using Skylab.Shared.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.ComponentGroup;

namespace Skylab.Api.Endpoints;

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

        group.MapGet("/{id:guid}/linkable-forms", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Veriyi görmek için giriş yapmalısınız.");

            var result = await service.GetLinkableFormsAsync(id, userId.Value, ct);
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

        group.MapGet("/responses/{id:guid}", async (Guid id, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Cevabı görmek için giriş yapmalısınız.");

            var result = await service.GetResponseByIdAsync(id, userId.Value, ct);
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

        group.MapGet("/component-groups", async (IComponentGroupService service, ICurrentUserService userService, [AsParameters] GetComponentGroupsRequest request, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Grupları görmek için giriş yapmalısınız.");

            var result = await service.GetUserGroupsAsync(userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/component-groups/{id:guid}", async (Guid id, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Grubu görmek için giriş yapmalısınız.");

            var result = await service.GetGroupByIdAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/component-groups", async ([FromBody] ComponentGroupUpsertRequest request, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Grup oluşturmak için giriş yapmalısınız.");

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
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Grup güncellemek için giriş yapmalısınız.");

            var result = await service.UpdateGroupAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/component-groups/{id:guid}", async (Guid id, IComponentGroupService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Grup silmek için giriş yapmalısınız.");

            var result = await service.DeleteGroupAsync(id, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });
    }
}
