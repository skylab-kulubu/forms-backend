using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Extensions;

public static class ErrorResponses
{
    /// <summary>
    /// Servisin sonuç olarak dönmediği hatalar da gövdeyle yanıtlanır: eşzamanlı iki yazmadan geç kalan 409, beklenmeyen
    /// her hata 500 alır. İç uçlar RFC 7807 konuşur, gerisi servislerin zarfını. IExceptionHandler yerine ayrı bir
    /// middleware, çünkü .NET 9'da işleyicinin yakaladığı her hata yine "unhandled exception" diye loglanır.
    /// </summary>
    public static IApplicationBuilder UseErrorResponses(this IApplicationBuilder app)
    {
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("Skylab.Forms.Api.Errors");

        return app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (StorageConflictException) when (!context.Response.HasStarted)
            {
                await WriteAsync(context, StatusCodes.Status409Conflict, "storage_conflict", new ServiceResult<object>(
                    ServiceStatus.Conflict,
                    Message: "Kayıt aynı anda başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin."));
            }
            catch (Exception ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
            {
                // Ham yol bir paylaşım token'ı taşıyabilir; log uç kalıbını yazar.
                logger.LogError(ex, "{Method} {Route} beklenmeyen bir hatayla bitti", context.Request.Method,
                    (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(eşleşen uç yok)");

                await WriteAsync(context, StatusCodes.Status500InternalServerError, "unexpected_error", new ServiceResult<object>(ServiceStatus.UnexpectedError));
            }
        });
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string problemCode, ServiceResult<object> envelope)
    {
        // CORS başlıkları OnStarting ile yazıldığı için Clear onları silmez.
        context.Response.Clear();

        var result = context.Request.Path.StartsWithSegments("/internal")
            ? Results.Problem(statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = problemCode })
            : envelope.ToApiResult();

        return result.ExecuteAsync(context);
    }
}
