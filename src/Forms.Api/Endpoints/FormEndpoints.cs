using Forms.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Forms.API.Endpoints;

public static class FormEndpoints
{
    private static readonly Guid FixedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static void MapFormEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("api/forms").WithTags("Forms");

        group.MapGet("/{id:guid}", async (Guid id, IFormService service, CancellationToken ct) =>
        {
            var result = await service.GetDisplayFormByIdAsync(id, FixedUserId, ct);

            return result.Status switch
            {
                FormDisplayStatus.Available => Results.Ok(result),

                FormDisplayStatus.NotFound => Results.NotFound(new { message = "Form bulunamadı." }),
                FormDisplayStatus.NotAvailable => Results.NotFound(new { message = "Form artık yayında değil." }),

                _ => Results.Ok(result)
            };
        });
    }
}