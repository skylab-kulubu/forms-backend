using Skylab.Forms.Application.Services;
using Skylab.Api.Extensions;
using Skylab.Shared.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Api.Endpoints;

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

            if (result.Status == ServiceStatus.Success)
                return Results.Created($"/api/forms/responses/{result.Data}", result);

             return result.ToApiResult();
        });
    }
}
