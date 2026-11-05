using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Api.Realtime;

public static class StudioRealtime
{
    private const int MaxMessageBytes = 1024 * 1024;

    public static IServiceCollection AddFormStudio(this IServiceCollection services)
    {
        services.AddSignalR(options => options.MaximumReceiveMessageSize = MaxMessageBytes)
            .AddHubOptions<StudioHub>(options => options.AddFilter<AccountGateHubFilter>());

        services.AddSingleton<AccountGateHubFilter>();
        services.AddSingleton<StudioPresence>();
        services.AddSingleton<IStudioNotifier, HubStudioNotifier>();

        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Events ??= new JwtBearerEvents();
            var received = options.Events.OnMessageReceived;

            options.Events.OnMessageReceived = async context =>
            {
                await received(context);

                if (string.IsNullOrEmpty(context.Token)
                    && context.HttpContext.Request.Path.StartsWithSegments(StudioHub.Path)
                    && context.Request.Query["access_token"] is { Count: > 0 } token)
                {
                    context.Token = token.ToString();
                }
            };
        });

        return services;
    }

    public static IEndpointRouteBuilder MapStudioHub(this IEndpointRouteBuilder routes)
    {
        routes.MapHub<StudioHub>(StudioHub.Path, options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();

        return routes;
    }
}
