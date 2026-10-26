using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Extensions;

public static class ErrorResponses
{
    /// <summary>
    /// Servisin sonuç olarak dönmediği hatalar da gövdeyle yanıtlanır: eşzamanlı iki yazmadan geç kalan 409, sunucunun
    /// okuyamadığı istek kendi durumunu (çoğunlukla 400), beklenmeyen her hata 500 alır. İç uçlar RFC 7807 konuşur, gerisi
    /// servislerin zarfını. IExceptionHandler yerine ayrı bir middleware, çünkü .NET 9'da işleyicinin yakaladığı her hata
    /// yine "unhandled exception" diye loglanır.
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
                await WriteAsync(context, StatusCodes.Status409Conflict, "storage_conflict", ServiceStatus.Conflict,
                    "Kayıt aynı anda başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin.");
            }
            catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
            {
                // Gövdesi yarım kalan, çok büyük ya da geç gelen istek istemcinin hatasıdır: Kestrel'in vereceği durum korunur,
                // loglanmaz.
                await WriteAsync(context, ex.StatusCode, "bad_request", ServiceStatus.NotAcceptable, ex.StatusCode switch
                {
                    StatusCodes.Status413PayloadTooLarge => "İstek gövdesi izin verilen boyutu aşıyor.",
                    StatusCodes.Status408RequestTimeout => "İstek gövdesi zamanında gelmedi.",
                    _ => "İstek okunamadı."
                });
            }
            catch (Exception ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
            {
                // Ham yol bir paylaşım token'ı taşıyabilir; log uç kalıbını yazar.
                logger.LogError(ex, "{Method} {Route} beklenmeyen bir hatayla bitti", context.Request.Method,
                    (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(eşleşen uç yok)");

                await WriteAsync(context, StatusCodes.Status500InternalServerError, "unexpected_error", ServiceStatus.UnexpectedError,
                    "Beklenmeyen bir hata oluştu. Biraz sonra tekrar deneyin.");
            }
        });
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string problemCode, ServiceStatus status, string message)
    {
        // CORS başlıkları OnStarting ile yazıldığı için Clear onları silmez.
        context.Response.Clear();

        var result = context.Request.Path.StartsWithSegments("/internal")
            ? Results.Problem(statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = problemCode })
            : Results.Json(new ServiceResult<object>(status, Message: message), statusCode: statusCode);

        return result.ExecuteAsync(context);
    }
}
