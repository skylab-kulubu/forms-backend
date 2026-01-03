using Forms.Application.Services;
using Forms.API.Extensions;
using Microsoft.AspNetCore.Mvc;
using Forms.Application.Contracts.Responses;

namespace Forms.API.Endpoints;

public static class FormEndpoints
{
    public static void MapFormEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/forms").WithTags("Forms");

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            var result = await service.GetDisplayFormByIdAsync(id, userId, ct);

            return result.ToApiResult();
        });

        group.MapPost("/responses", async ([FromBody] ResponseSubmitRequest request, IFormResponseService service, ICurrentUserService userService, CancellationToken ct) =>
        {
            var userId = await userService.GetUserIdAsync(ct);
            var result = await service.SubmitResponseAsync(request, userId, ct);

            if (result.Status == FormAccessStatus.Available)
                return Results.Created($"/api/forms/responses/{result.Data}", result);
        
             return result.ToApiResult();
        });
    }
}