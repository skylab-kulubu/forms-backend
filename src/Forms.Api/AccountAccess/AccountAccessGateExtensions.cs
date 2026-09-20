using Skylab.Forms.Infrastructure.AccountAccess;

namespace Skylab.Forms.Api.AccountAccess;

public static class AccountAccessGateExtensions
{
    public static IApplicationBuilder UseAccountAccessGate(this IApplicationBuilder app) =>
        app.UseMiddleware<AccountAccessGateMiddleware>();

    public static IEndpointRouteBuilder MapAccountAccessHealthEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithTags("Health")
            .WithMetadata(AccountAccessGateBypassMetadata.Instance);

        endpoints.MapGet(
                "/health/ready",
                async (HttpContext context, IAccountAccessGate gate, AccountAccessGateOptions options) =>
                {
                    var ready = await gate.IsContractReadyAsync(context.RequestAborted);
                    context.Response.Headers.CacheControl = "no-store";
                    if (ready)
                    {
                        await Results.Ok(new { status = "ready" }).ExecuteAsync(context);
                        return;
                    }

                    context.Response.Headers.RetryAfter = options.RetryAfterSeconds.ToString();
                    await Results.Json(
                            new { status = "not_ready" },
                            statusCode: StatusCodes.Status503ServiceUnavailable)
                        .ExecuteAsync(context);
                })
            .WithTags("Health")
            .WithMetadata(AccountAccessGateBypassMetadata.Instance);

        return endpoints;
    }
}
