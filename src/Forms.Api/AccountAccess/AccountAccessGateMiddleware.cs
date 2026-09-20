using Skylab.Forms.Infrastructure.AccountAccess;

namespace Skylab.Forms.Api.AccountAccess;

public sealed class AccountAccessGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IAccountAccessGate gate,
        AccountAccessGateOptions options,
        ILogger<AccountAccessGateMiddleware> logger)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<AccountAccessGateBypassMetadata>() is not null ||
            gate.Mode == AccountAccessGateMode.Off ||
            context.User.Identities.All(identity => !identity.IsAuthenticated))
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

        if (subjects.Length != 1 || subjects[0].Length > 512)
        {
            LogDecision(logger, context, "blocked");
            await WriteUnauthorizedAsync(context);
            return;
        }

        var decision = await gate.CheckSubjectAsync(subjects[0], context.RequestAborted);
        switch (decision)
        {
            case AccountAccessDecision.Allowed:
                await next(context);
                return;
            case AccountAccessDecision.Blocked:
                LogDecision(logger, context, "blocked");
                await WriteUnauthorizedAsync(context);
                return;
            case AccountAccessDecision.Unavailable:
                LogDecision(logger, context, "unavailable");
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.RetryAfter = options.RetryAfterSeconds.ToString();
                return;
            default:
                throw new InvalidOperationException("Unknown account access decision.");
        }
    }

    private static Task WriteUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private static void LogDecision(
        ILogger logger,
        HttpContext context,
        string decision)
    {
        logger.LogWarning(
            "Account access gate decision {Decision}; correlation {CorrelationId}",
            decision,
            context.TraceIdentifier);
    }
}

public sealed class AccountAccessGateBypassMetadata
{
    public static AccountAccessGateBypassMetadata Instance { get; } = new();

    private AccountAccessGateBypassMetadata()
    {
    }
}
