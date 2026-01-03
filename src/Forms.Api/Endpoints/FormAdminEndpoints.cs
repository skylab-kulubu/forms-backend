using Forms.Application.Services;
using Forms.API.Extensions;
using Microsoft.AspNetCore.Mvc;
using Forms.Application.Contracts.Forms;
using Forms.Application.Contracts.Responses;

namespace Forms.API.Endpoints;

public static class FormAdminEndpoints
{
    public static void MapFormAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/admin/forms/").WithTags("FormsAdmin");

        group.MapGet("/", async (IFormService service, ICurrentUserService userService, [AsParameters] GetUserFormsRequest request, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Formları görmek için giriş yapmalısınız.");

            var result = await service.GetUserFormsAsync(userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Formu görmek için giriş yapmalısınız.");

            var result = await service.GetFormByIdAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapGet("/{id:guid}/linkable-forms", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Veriyi görmek için giriş yapmalısınız.");

            var result = await service.GetLinkableFormsAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });

        group.MapPost("/", async ([FromBody] FormUpsertRequest request, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Form oluşturmak için giriş yapmalısınız.");

            var result = await service.UpsertFormAsync(request, userId.Value, ct);

            if (result.Status == FormAccessStatus.Available && result.Data != null)
            {
                if (!request.Id.HasValue) return Results.Created($"/api/admin/forms/{result.Data.Id}", result);
            }

            return result.ToApiResult();
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Form silmek için giriş yapmalısınız.");

            var result = await service.DeleteFormAsync(id, userId.Value, ct);
            return result.Status == FormAccessStatus.Available ? Results.NoContent() : result.ToApiResult();
        });

        group.MapGet("/{id:guid}/responses", async (Guid id, [AsParameters] GetResponsesRequest request, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Cevapları görmek için giriş yapmalısınız.");

            var result = await service.GetFormResponsesAsync(id, userId.Value, request, ct);
            return result.ToApiResult();
        });

        group.MapGet("/responses/{id:guid}", async (Guid id, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Cevabı görmek için giriş yapmalısınız.");

            var result = await service.GetResponseByIdAsync(id, userId.Value, ct);
            return result.ToApiResult();
        });
        
        group.MapPatch("/responses/{id:guid}/status", async (Guid id, [FromBody] ResponseStatusUpdateRequest request, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            if (userId == null) return FormAccessStatus.Unauthorized.ToApiResult("Durumu güncellemek için giriş yapmalısınız.");

            var serviceContract = new ResponseStatusUpdateRequest(id, request.NewStatus, request.Note);
            
            var result = await service.UpdateResponseStatusAsync(serviceContract, userId.Value, ct);
            return result.ToApiResult();
        }); 
    }
}