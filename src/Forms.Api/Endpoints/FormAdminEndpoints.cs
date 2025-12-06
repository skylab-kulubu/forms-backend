using Forms.Application.Contracts;
using Forms.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Forms.API.Endpoints;

public static class FormAdminEndpoints
{
    private static readonly Guid FixedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static void MapFormAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/forms/admin").WithTags("FormsAdmin");

        group.MapGet("/", async (IFormService service, CancellationToken ct) =>
        {
            var forms = await service.GetUserFormsAsync(FixedUserId, ct);
            return Results.Ok(forms);
        });

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            try
            {
                var form = await service.GetFormByIdAsync(id, FixedUserId, ct);
                return form is not null ? Results.Ok(form) : Results.NotFound("Form not found.");
            }
            catch (UnauthorizedAccessException)
            {
                return Results.StatusCode(403);
            }
        });

        group.MapPost("/", async ([FromBody] FormUpsertContract request, IFormService service, CancellationToken ct) =>
        {
            var result = await service.UpsertFormAsync(request, FixedUserId, ct);

            if (request.Id.HasValue) return Results.Ok(result);

            return Results.Created($"/api/group/{result.Id}", result);

        });

        group.MapDelete("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var success = await service.DeleteFormAsync(id, FixedUserId, ct);

            return success ? Results.NoContent() : Results.NotFound("Form not found.");
        });
    }
}