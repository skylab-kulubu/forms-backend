using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Ownership;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Ownership;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Skylab.Forms.Api.Endpoints;

public static class WorkflowAdminEndpoints
{
    public static void MapWorkflowAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/workflows").WithTags("WorkflowsAdmin");

        group.MapGet("/", async (IFormWorkflowService service, ICurrentUser user, [AsParameters] GetWorkflowsRequest request, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışları görmek için giriş yapmalısınız.");

            var result = await service.GetOwnedAsync(userId.Value, request, ct);
            return result.ToApiResult();
        });

        // Hesabı silinen kişilerden kalan, sahibi Silinmiş kullanıcı olan akışlar.
        group.MapGet("/orphaned", async (IFormWorkflowService service, ICurrentUser user, [AsParameters] GetWorkflowsRequest request, CancellationToken ct) =>
        {
            if (!user.IsPlatformAdmin)
                return ServiceStatus.NotAuthorized.ToApiResult();

            var result = await service.GetOwnedAsync(DeletedUser.Id, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/available-forms", async (IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Formları görmek için giriş yapmalısınız.");

            var result = await service.GetCandidateFormsAsync(userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı görmek için giriş yapmalısınız.");

            var result = await service.GetAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/versions", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Sürümleri görmek için giriş yapmalısınız.");

            var result = await service.GetVersionsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/runs/overview", async (Guid id, IFormWorkflowRunService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Başvuruları görmek için giriş yapmalısınız.");

            var result = await service.GetOverviewAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/runs", async (Guid id, IFormWorkflowRunService service, ICurrentUser user, [AsParameters] GetWorkflowRunsRequest request, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Başvuruları görmek için giriş yapmalısınız.");

            var result = await service.GetRunsAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] WorkflowUpsertRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akış oluşturmak için giriş yapmalısınız.");

            var result = await service.CreateAsync(request, userId.Value, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
                return Results.Created($"/api/admin/workflows/{result.Data.Id}", result);

            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] WorkflowUpsertRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akış güncellemek için giriş yapmalısınız.");

            var result = await service.UpdateAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/intake", async (Guid id, [FromBody] WorkflowIntakeRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışın başvuru durumunu değiştirmek için giriş yapmalısınız.");

            var result = await service.UpdateIntakeAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/intake-schedule", async (Guid id, [FromBody] WorkflowIntakeScheduleRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışın kapanış saatini değiştirmek için giriş yapmalısınız.");

            var result = await service.UpdateIntakeScheduleAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/definition", async (Guid id, [FromBody] WorkflowDefinitionRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akış düzenlemek için giriş yapmalısınız.");

            var result = await service.UpdateDefinitionAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/available-forms", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Formları görmek için giriş yapmalısınız.");

            var result = await service.GetAvailableFormsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/validate", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı doğrulamak için giriş yapmalısınız.");

            var result = await service.ValidateAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/publish", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı yayınlamak için giriş yapmalısınız.");

            var result = await service.PublishAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı arşivlemek için giriş yapmalısınız.");

            var result = await service.ArchiveAsync(id, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapPost("/{id:guid}/transfer", async (Guid id, [FromBody] OwnershipTransferRequest request, IOwnershipService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı devretmek için giriş yapmalısınız.");

            var result = await service.TransferWorkflowAsync(id, request.UserId, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/runs/{instanceId:guid}/void", async (Guid instanceId, [FromBody] WorkflowRunVoidRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.Id;
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Başvuruyu yeniden açmak için giriş yapmalısınız.");

            var result = await service.VoidRunAsync(instanceId, request, userId.Value, ct);
            return result.ToApiResult();
        });
    }
}
