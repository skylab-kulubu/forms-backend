using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.AccountAccess;

public static class AccountAccessGateRegistration
{
    public static IServiceCollection AddAccountAccessGate(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = AccountAccessGateOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        if (options.Mode == AccountAccessGateMode.Off)
        {
            services.AddSingleton<IAccountAccessGate, DisabledAccountAccessGate>();
            return services;
        }

        services.AddKeyedSingleton<IConnectionMultiplexer>(
            AccountAccessGateContract.RedisConnectionName,
            (_, _) => ConnectionMultiplexer.Connect(options.ToRedisConfiguration()));
        services.AddSingleton<IAccountAccessGate>(serviceProvider =>
            new RedisAccountAccessGate(
                serviceProvider.GetRequiredKeyedService<IConnectionMultiplexer>(
                    AccountAccessGateContract.RedisConnectionName),
                options));

        return services;
    }
}
