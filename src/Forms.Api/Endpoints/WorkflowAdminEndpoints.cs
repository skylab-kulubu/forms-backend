using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Microsoft.AspNetCore.Mvc;

namespace Skylab.Forms.Api.Endpoints;

public static class WorkflowAdminEndpoints
{
    public static void MapWorkflowAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/workflows").WithTags("WorkflowsAdmin");

        group.MapGet("/", async (IFormWorkflowService service, ICurrentUserService userService, [AsParameters] GetWorkflowsRequest request, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışları görmek için giriş yapmalısınız.");

            var result = await service.GetOwnedAsync(userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı görmek için giriş yapmalısınız.");

            var result = await service.GetAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/versions", async (Guid id, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Sürümleri görmek için giriş yapmalısınız.");

            var result = await service.GetVersionsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] WorkflowUpsertRequest request, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akış oluşturmak için giriş yapmalısınız.");

            var result = await service.CreateAsync(request, userId.Value, ct);

            if (result.Status == ServiceStatus.Success && result.Data != null)
                return Results.Created($"/api/admin/workflows/{result.Data.Id}", result);

            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}", async (Guid id, [FromBody] WorkflowUpsertRequest request, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akış güncellemek için giriş yapmalısınız.");

            var result = await service.UpdateAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPut("/{id:guid}/definition", async (Guid id, [FromBody] WorkflowDefinitionRequest request, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akış düzenlemek için giriş yapmalısınız.");

            var result = await service.UpdateDefinitionAsync(id, request, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/available-forms", async (Guid id, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Formları görmek için giriş yapmalısınız.");

            var result = await service.GetAvailableFormsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/validate", async (Guid id, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı doğrulamak için giriş yapmalısınız.");

            var result = await service.ValidateAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/{id:guid}/publish", async (Guid id, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı yayınlamak için giriş yapmalısınız.");

            var result = await service.PublishAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormWorkflowService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return ServiceStatus.Unauthorized.ToApiResult("Akışı arşivlemek için giriş yapmalısınız.");

            var result = await service.ArchiveAsync(id, userId.Value, ct);
            return result.Status == ServiceStatus.Success ? Results.NoContent() : result.ToApiResult();
        });
    }
}
