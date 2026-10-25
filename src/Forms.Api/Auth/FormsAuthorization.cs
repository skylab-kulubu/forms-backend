using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Skylab.Forms.Api.Extensions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Infrastructure.Auth;

namespace Skylab.Forms.Api.Auth;

public static class FormsAuthorization
{
    public const string PlatformAdmin = "platform-admin";

    public static IServiceCollection AddFormsAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => Guid.TryParse(context.User.FindFirst("sub")?.Value, out _))
                .Build())
            .AddPolicy(PlatformAdmin, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => FormsPermissions.Has(context.User, FormsPermissions.PlatformAdmin)));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, EnvelopeAuthorizationResultHandler>();

        return services;
    }

    public static Guid SignedInId(this ICurrentUser user) =>
        user.Id ?? throw new InvalidOperationException("Bu uç RequireAuthorization ile korunmalı.");

    private sealed class EnvelopeAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _default = new();

        public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
        {
            if (result.Challenged || result.Forbidden)
            {
                var (status, message) = result.Challenged
                    ? (ServiceStatus.Unauthorized, "Bu işlem için giriş yapmalısınız.")
                    : (ServiceStatus.NotAuthorized, "Bu işlem için yetkiniz yok.");

                if (result.Challenged) context.Response.Headers.WWWAuthenticate = "Bearer";
                await status.ToApiResult(message).ExecuteAsync(context);

                return;
            }

            await _default.HandleAsync(next, context, policy, result);
        }
    }
}
