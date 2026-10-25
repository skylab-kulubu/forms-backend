using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Extensions;

public static class StorageConflictResponses
{
    /// <summary>
    /// Eşzamanlı iki yazmadan geç kalanı, servislerin zarfıyla 409 alır. IExceptionHandler yerine ayrı bir
    /// middleware, çünkü .NET 9'da işleyicinin yakaladığı her hata yine "unhandled exception" diye loglanır.
    /// </summary>
    public static IApplicationBuilder UseStorageConflictResponses(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (StorageConflictException) when (!context.Response.HasStarted)
            {
                // CORS başlıkları OnStarting ile yazıldığı için Clear onları silmez.
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status409Conflict;

                await context.Response.WriteAsJsonAsync(new ServiceResult<object>(
                    ServiceStatus.Conflict,
                    Message: "Kayıt aynı anda başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin."));
            }
        });
}
