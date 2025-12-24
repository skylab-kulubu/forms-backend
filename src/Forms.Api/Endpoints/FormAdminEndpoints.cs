using Forms.Application.Contracts;
using Forms.Application.Services;
using Forms.API.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Forms.API.Endpoints;

public static class FormAdminEndpoints
{
    private static readonly Guid FixedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static void MapFormAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/forms/").WithTags("FormsAdmin");

        group.MapGet("/", async (IFormService service, CancellationToken ct) =>
        {
            var result = await service.GetUserFormsAsync(FixedUserId, ct);
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

        group.MapPost("/", async ([FromBody] FormUpsertContract request, IFormService service, CancellationToken ct) =>
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

        group.MapGet("/{formId:guid}/responses", async (Guid formId, IFormResponseService service, CancellationToken ct) =>
        {
            var result = await service.GetFormResponsesAsync(formId, FixedUserId, ct);
            return result.ToApiResult();
        });

        group.MapGet("/responses/{id:guid}", async (Guid id, IFormResponseService service, CancellationToken ct) =>
        {
            var result = await service.GetResponseByIdAsync(id, FixedUserId, ct);
            return result.ToApiResult();
        });
        
        group.MapPatch("/responses/{id:guid}/status", async (Guid id, [FromBody] FormResponseStatusUpdateContract request, IFormResponseService service, CancellationToken ct) =>
        {
            var serviceContract = new FormResponseStatusUpdateContract(id, request.NewStatus);
            
            var result = await service.UpdateResponseStatusAsync(serviceContract, FixedUserId, ct);
            return result.ToApiResult();
        }); 
    }
}