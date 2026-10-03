namespace Skylab.Forms.Api.AccountErasure;

public static class InternalRouteGuardExtensions
{
    /// <summary>
    /// /internal altındaki rotalar yalnız iç Docker ağından çağrılır. Traefik'ten gelen
    /// istek X-Forwarded-*, Forwarded ya da X-Real-Ip taşır ve token'a bakılmadan 404 alır.
    /// Asıl güvenlik sınırı yine token'dır; bu kontrol rotanın dışarıdan görünmemesi için.
    /// </summary>
    public static IApplicationBuilder UseInternalRouteGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/internal") && CameThroughProxy(context.Request.Headers))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                context.Response.Headers.CacheControl = "no-store";
                return;
            }

            await next(context);
        });

    private static bool CameThroughProxy(IHeaderDictionary headers) =>
        headers.Keys.Any(name =>
            name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("X-Real-Ip", StringComparison.OrdinalIgnoreCase));
}
