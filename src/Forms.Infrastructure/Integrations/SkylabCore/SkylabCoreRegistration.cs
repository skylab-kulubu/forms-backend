using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Integrations.Oidc;
using Skylab.Forms.Infrastructure.ResponseNotifications;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

public static class SkylabCoreRegistration
{
    public static IServiceCollection AddSkylabCore(this IServiceCollection services, IConfiguration configuration)
    {
        var gate = AccountAccessGateOptions.FromConfiguration(configuration);
        services.AddSingleton(gate);
        if (gate.Mode == AccountAccessGateMode.Enforce)
            services.AddSingleton(_ => new AccountAccessGate(ConnectionMultiplexer.Connect(gate.ToRedisConfiguration()), gate));

        var baseUrl = new Uri(configuration["Services:Users:BaseUrl"] ?? "http://core:8080");

        services.AddHttpClient<IUserDirectory, CoreUserDirectory>(client =>
        {
            client.BaseAddress = baseUrl;
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<IShortLinkProvider, CoreShortLinkProvider>(client =>
        {
            client.BaseAddress = baseUrl;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<IAnswerFileStore, CoreAnswerFileStore>(client =>
        {
            client.BaseAddress = baseUrl;
            client.Timeout = TimeSpan.FromSeconds(60);
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient(ResponseNotificationWorker.HttpClientName, client =>
        {
            client.BaseAddress = baseUrl;
            client.Timeout = TimeSpan.FromSeconds(10);
        }).AddHttpMessageHandler<ServiceTokenHandler>();
        services.AddHostedService<ResponseNotificationWorker>();

        return services;
    }
}
