using Skylab.Forms.Infrastructure.AccountAccess;

namespace Skylab.Forms.Api.AccountAccess;

public static class AccountAccessGateExtensions
{
    /// <summary>
    /// Doğrulanmış kimliği taşıyan her isteği endpoint'ten önce engel listesine sorar. Anonim istek
    /// sorgulanmaz; liste okunamazsa istek geçmez, 503 alır.
    /// </summary>
    public static IApplicationBuilder UseAccountAccessGate(this IApplicationBuilder app)
    {
        if (app.ApplicationServices.GetService<AccountAccessGate>() is not { } gate) return app;

        var options = app.ApplicationServices.GetRequiredService<AccountAccessGateOptions>();
        var logger = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("AccountAccessGate");

        return app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<BypassGate>() is not null || !context.User.Identities.Any(identity => identity.IsAuthenticated))
            {
                await next(context);
                return;
            }

            var subjects = context.User.Identities
                .Where(identity => identity.IsAuthenticated)
                .SelectMany(identity => identity.FindAll("sub"))
                .Select(claim => claim.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var decision = subjects is [{ Length: <= 512 } subject]
                ? await gate.CheckSubjectAsync(subject, context.RequestAborted)
                : AccountAccessDecision.Blocked;

            if (decision == AccountAccessDecision.Allowed)
            {
                await next(context);
                return;
            }

            // Log yalnız kararı ve isteğin kimliğini taşır; subject ya da özeti yazılmaz.
            logger.LogWarning(
                "Account access gate decision {Decision}; correlation {CorrelationId}",
                decision == AccountAccessDecision.Blocked ? "blocked" : "unavailable",
                context.TraceIdentifier);

            context.Response.Headers.CacheControl = "no-store";

            if (decision == AccountAccessDecision.Blocked)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate = "Bearer";
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = options.RetryAfterSeconds.ToString();
            }
        });
    }

    public static IEndpointRouteBuilder MapAccountAccessHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var gate = endpoints.ServiceProvider.GetService<AccountAccessGate>();
        var options = endpoints.ServiceProvider.GetRequiredService<AccountAccessGateOptions>();

        endpoints.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithTags("Health")
            .WithMetadata(BypassGate.Instance);

        endpoints.MapGet("/health/ready", async (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";

                if (gate is null || await gate.IsContractReadyAsync(context.RequestAborted))
                    return Results.Ok(new { status = "ready" });

                context.Response.Headers.RetryAfter = options.RetryAfterSeconds.ToString();
                return Results.Json(new { status = "not_ready" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
            .WithTags("Health")
            .WithMetadata(BypassGate.Instance);

        return endpoints;
    }

    private sealed class BypassGate
    {
        public static readonly BypassGate Instance = new();
    }
}
