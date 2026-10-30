using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Extensions;

public static class ErrorResponses
{
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
                await WriteAsync(context, ex.StatusCode, "bad_request", ServiceStatus.NotAcceptable, ex.StatusCode switch
                {
                    StatusCodes.Status413PayloadTooLarge => "İstek gövdesi izin verilen boyutu aşıyor.",
                    StatusCodes.Status408RequestTimeout => "İstek gövdesi zamanında gelmedi.",
                    _ => "İstek okunamadı."
                });
            }
            catch (Exception ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
            {
                logger.LogError(ex, "{Method} {Route} beklenmeyen bir hatayla bitti", context.Request.Method,
                    (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(eşleşen uç yok)");

                await WriteAsync(context, StatusCodes.Status500InternalServerError, "unexpected_error", ServiceStatus.UnexpectedError,
                    "Beklenmeyen bir hata oluştu. Biraz sonra tekrar deneyin.");
            }
        });
    }

    private static Task WriteAsync(HttpContext context, int statusCode, string problemCode, ServiceStatus status, string message)
    {
        context.Response.Clear();

        var result = context.Request.Path.StartsWithSegments("/internal")
            ? Results.Problem(statusCode: statusCode, extensions: new Dictionary<string, object?> { ["code"] = problemCode })
            : Results.Json(new ServiceResult<object>(status, Message: message), statusCode: statusCode);

        return result.ExecuteAsync(context);
    }
}
