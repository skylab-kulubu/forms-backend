using Microsoft.AspNetCore.SignalR;
using Skylab.Forms.Api.AccountAccess;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Api.Realtime;

public sealed class AccountGateHubFilter(TimeProvider time, ILogger<AccountGateHubFilter> logger) : IHubFilter
{
    private const string AllowedAtKey = "account-gate-allowed-at";
    private static readonly TimeSpan CursorTrust = TimeSpan.FromSeconds(5);

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var connection = invocationContext.Context;

        if (invocationContext.ServiceProvider.GetService<IAccountAccessPolicy>() is not { } policy || connection.User is not { } user)
            return await next(invocationContext);

        var now = time.GetUtcNow();

        if (invocationContext.HubMethodName == nameof(StudioHub.Cursor)
            && connection.Items.TryGetValue(AllowedAtKey, out var stored) && stored is DateTimeOffset allowedAt && now - allowedAt < CursorTrust)
            return await next(invocationContext);

        var decision = await AccountAccessGateExtensions.DecideAsync(policy, user, connection.ConnectionAborted);
        if (decision == AccountAccessDecision.Allowed)
        {
            connection.Items[AllowedAtKey] = now;
            return await next(invocationContext);
        }

        logger.LogWarning(
            "Account access gate decision {Decision}; connection {ConnectionId}",
            decision == AccountAccessDecision.Blocked ? "blocked" : "unavailable",
            connection.ConnectionId);

        if (decision == AccountAccessDecision.Blocked)
        {
            connection.Abort();
            return new ServiceResult<object>(ServiceStatus.Unauthorized, Message: "Bu işlem için giriş yapmalısınız.");
        }

        return new ServiceResult<object>(ServiceStatus.ServiceUnavailable, Message: "Hesap durumu şu an doğrulanamıyor; biraz sonra tekrar deneyin.");
    }
}
