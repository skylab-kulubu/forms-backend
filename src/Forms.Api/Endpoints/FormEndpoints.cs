using Forms.Application.Contracts;
using Forms.Application.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Forms.API.Endpoints;

public static class FormEndpoints
{
    private static readonly Guid FixedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static void MapFormEndpoints(this IEndpointRouteBuilder routes)
    {
        var forms = routes.MapGroup("api/forms").WithTags("Forms");

        forms.MapGet("/", async (IFormService service, CancellationToken ct) =>
        {
            var forms = await service.GetUserFormsAsync(FixedUserId, ct);
            return Results.Ok(forms);
        });

        forms.MapGet("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var form = await service.GetByIdAsync(id, ct);
            return form is not null ? Results.Ok(form) : Results.NotFound("Form not found with given id.");
        });

        forms.MapPost("/", async ([FromBody] FormUpsertContract request, IFormService service, CancellationToken ct) =>
        {
            var result = await service.UpsertAsync(request, FixedUserId, ct);
            
            if (request.Id.HasValue) return Results.Ok(result);

            return Results.Created($"/api/forms/{result.Id}", result);

        });

        forms.MapDelete("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var success = await service.DeleteAsync(id, FixedUserId, ct);

            return success ? Results.NoContent() : Results.NotFound("Form not found.");
        });
    }
}