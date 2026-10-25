using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Extensions;

public static class StorageConflictResponses
{
    public static IApplicationBuilder UseStorageConflictResponses(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (StorageConflictException) when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status409Conflict;

                await context.Response.WriteAsJsonAsync(new ServiceResult<object>(
                    ServiceStatus.Conflict,
                    Message: "Kayıt aynı anda başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin."));
            }
        });
}
