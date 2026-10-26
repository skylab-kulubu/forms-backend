using Skylab.Forms.Api.Auth;
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
    private const string WorkflowRoute = "AdminWorkflow";

    public static void MapWorkflowAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/workflows").WithTags("WorkflowsAdmin").RequireAuthorization();

        group.MapGet("/", async (IFormWorkflowService service, ICurrentUser user, [AsParameters] GetWorkflowsRequest request, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetOwnedAsync(userId, request, ct);
            return result.ToApiResult();
        });

        // Hesabı silinen kişilerden kalan, sahibi Silinmiş kullanıcı olan akışlar.
        group.MapGet("/orphaned", async (IFormWorkflowService service, [AsParameters] GetWorkflowsRequest request, CancellationToken ct) =>
        {
            var result = await service.GetOwnedAsync(DeletedUser.Id, request, ct);
            return result.ToApiResult();
        }).RequireAuthorization(FormsAuthorization.PlatformAdmin);

        group.MapGet("/available-forms", async (IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetCandidateFormsAsync(userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetAsync(id, userId, ct);
            return result.ToApiResult();
        }).WithName(WorkflowRoute);

        group.MapGet("/{id:guid}/versions", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetVersionsAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/runs/overview", async (Guid id, IFormWorkflowRunService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetOverviewAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/runs", async (Guid id, IFormWorkflowRunService service, ICurrentUser user, [AsParameters] GetWorkflowRunsRequest request, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetRunsAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] WorkflowUpsertRequest request, IFormWorkflowService service, ICurrentUser user, HttpContext httpContext, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.CreateAsync(request, userId, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
                return result.ToCreatedResult(httpContext, WorkflowRoute, new { id = result.Data.Id });

            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] WorkflowUpsertRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/intake", async (Guid id, [FromBody] WorkflowIntakeRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateIntakeAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/intake-schedule", async (Guid id, [FromBody] WorkflowIntakeScheduleRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateIntakeScheduleAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/definition", async (Guid id, [FromBody] WorkflowDefinitionRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.UpdateDefinitionAsync(id, request, userId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/available-forms", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.GetAvailableFormsAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/validate", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.ValidateAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/publish", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.PublishAsync(id, userId, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.ArchiveAsync(id, userId, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });

        group.MapPost("/{id:guid}/transfer", async (Guid id, [FromBody] OwnershipTransferRequest request, IOwnershipService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.TransferWorkflowAsync(id, request.UserId, userId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/runs/{instanceId:guid}/void", async (Guid instanceId, [FromBody] WorkflowRunVoidRequest request, IFormWorkflowService service, ICurrentUser user, CancellationToken ct) =>
        {
            var userId = user.SignedInId();

            var result = await service.VoidRunAsync(instanceId, request, userId, ct);
            return result.ToApiResult();
        });
    }
}
