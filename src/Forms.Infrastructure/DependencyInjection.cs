using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Infrastructure.Auth;
using Skylab.Forms.Infrastructure.Caching;
using Skylab.Forms.Infrastructure.Exports;
using Skylab.Forms.Infrastructure.Mail;
using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Infrastructure.Storage.Repositories;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")
            ?? configuration.GetConnectionString("Forms")
            ?? throw new InvalidOperationException("Forms database connection string is not configured.");

        var redisConnection = Environment.GetEnvironmentVariable("Redis__ConnectionString")
            ?? configuration["Redis:ConnectionString"]
            ?? "localhost:6379,defaultDatabase=1";

        services.AddDbContext<FormsDbContext>(options =>
        {
            options.UseNpgsql(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorCodesToAdd: ["28P01"]);
            });
        });

        services.AddScoped<IFormRepository, FormRepository>();
        services.AddScoped<IFormResponseRepository, FormResponseRepository>();
        services.AddScoped<IComponentGroupRepository, ComponentGroupRepository>();
        services.AddScoped<IFormMetricsRepository, FormMetricsRepository>();
        services.AddScoped<IFormWorkflowRepository, FormWorkflowRepository>();
        services.AddScoped<IFormWorkflowInstanceRepository, FormWorkflowInstanceRepository>();
        services.AddScoped<IFormsUnitOfWork, FormsUnitOfWork>();

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddScoped<IExcelService, ExcelService>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, JwtCurrentUserService>();

        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));
        services.AddHttpClient("keycloak");
        services.AddSingleton<IServiceTokenProvider, KeycloakServiceTokenProvider>();
        services.AddTransient<ServiceTokenHandler>();

        services.AddHttpClient<IExternalUserService, ExternalUserService>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Users:BaseUrl"] ?? "http://core:8080");
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<ICoreEventLookup, CoreEventLookup>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Users:BaseUrl"] ?? "http://core:8080");
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<ISkyMailService, SkyMailClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:SkyMail:BaseUrl"] ?? "http://skymail:3000/v1/");
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddSingleton<ChannelMailDispatcher>();
        services.AddSingleton<IMailDispatcher>(sp => sp.GetRequiredService<ChannelMailDispatcher>());
        services.AddHostedService<MailWorker>();

        services.Configure<FormMailOptions>(configuration.GetSection(FormMailOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FormMailOptions>>().Value);
        services.AddHostedService<PendingResponseReminderWorker>();

        return services;
    }

    public static async Task ApplyDatabaseMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
