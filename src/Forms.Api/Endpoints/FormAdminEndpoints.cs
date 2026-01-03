using Forms.Application.Services;
using Forms.API.Extensions;
using Microsoft.AspNetCore.Mvc;
using Forms.Application.Contracts.Forms;
using Forms.Application.Contracts.Responses;

namespace Forms.API.Endpoints;

public static class FormAdminEndpoints
{
    private static readonly Guid FixedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static void MapFormAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/forms/").WithTags("FormsAdmin");

        group.MapGet("/", async (IFormService service, [AsParameters] GetUserFormsRequest request, CancellationToken ct) =>
        {
            var result = await service.GetUserFormsAsync(FixedUserId, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var result = await service.GetFormByIdAsync(id, FixedUserId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/linkable-forms", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var result = await service.GetLinkableFormsAsync(id, FixedUserId, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] FormUpsertRequest request, IFormService service, CancellationToken ct) =>
        {
            var result = await service.UpsertFormAsync(request, FixedUserId, ct);

            if (result.Status == FormAccessStatus.Available && result.Data != null)
            {
                if (!request.Id.HasValue) return Results.Created($"/api/admin/forms/{result.Data.Id}", result);
            }

            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var result = await service.DeleteFormAsync(id, FixedUserId, ct);

            return result.Status == FormAccessStatus.Available ? Results.NoContent() : result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses", async (Guid id, [AsParameters] GetResponsesRequest request, IFormResponseService service, CancellationToken ct) =>
        {
            var result = await service.GetFormResponsesAsync(id, FixedUserId, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/responses/{id:guid}", async (Guid id, IFormResponseService service, CancellationToken ct) =>
        {
            var result = await service.GetResponseByIdAsync(id, FixedUserId, ct);
            return result.ToApiResult();
        });
        
        group.MapPatch("/responses/{id:guid}/status", async (Guid id, [FromBody] ResponseStatusUpdateRequest request, IFormResponseService service, CancellationToken ct) =>
        {
            var serviceContract = new ResponseStatusUpdateRequest(id, request.NewStatus, request.Note);
            
            var result = await service.UpdateResponseStatusAsync(serviceContract, FixedUserId, ct);
            return result.ToApiResult();
        }); 
    }
}