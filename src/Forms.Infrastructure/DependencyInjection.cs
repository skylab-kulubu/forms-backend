using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Application.ShortLinks;
using Skylab.Forms.Infrastructure.AnswerFiles;
using Skylab.Forms.Infrastructure.Attempts;
using Skylab.Forms.Infrastructure.Auth;
using Skylab.Forms.Infrastructure.Caching;
using Skylab.Forms.Infrastructure.Exports;
using Skylab.Forms.Infrastructure.GuestUploads;
using Skylab.Forms.Infrastructure.Integrations;
using Skylab.Forms.Infrastructure.Mail;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Infrastructure.Storage.Repositories;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Forms'un kendi altyapısı; dış sağlayıcılar Integrations altındaki AddXxx'lerle ayrıca takılır.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>()
            .Configure<IConfiguration>((storage, configuration) =>
            {
                storage.Database = configuration.GetConnectionString("Forms") ?? "";
                storage.Redis = configuration["Redis:ConnectionString"] ?? "";
            })
            .Validate(storage => storage.Database.Length > 0, "ConnectionStrings:Forms must hold the PostgreSQL connection string.")
            .Validate(storage => storage.Redis.Length > 0, "Redis:ConnectionString must hold the Redis connection string.")
            .ValidateOnStart();

        services.AddDbContext<FormsDbContext>((provider, options) =>
        {
            options.UseNpgsql(provider.GetRequiredService<IOptions<StorageOptions>>().Value.Database, sqlOptions =>
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
        services.AddScoped<IFormAttemptRepository, FormAttemptRepository>();
        services.AddScoped<IAccountErasureRepository, AccountErasureRepository>();
        services.AddScoped<IFormsUnitOfWork, FormsUnitOfWork>();

        services.AddSingleton<IConnectionMultiplexer>(provider =>
            ConnectionMultiplexer.Connect(provider.GetRequiredService<IOptions<StorageOptions>>().Value.Redis));
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddScoped<IExcelService, ExcelService>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddOptions<ShortLinkOptions>().BindConfiguration(ShortLinkOptions.SectionName);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<ShortLinkOptions>>().Value);

        services.AddOptions<GuestUploadOptions>()
            .BindConfiguration(GuestUploadOptions.SectionName)
            .Validate(guests => guests.Mode.Trim().ToLowerInvariant() is "on" or "off", "GuestUploads:Mode must be exactly 'off' or 'on'.")
            .Validate(guests => guests.IpSubmitsPerMinute is >= 1 and <= 100_000, "GuestUploads:IpSubmitsPerMinute must be between 1 and 100000.")
            .Validate(guests => guests.FormSubmitsPerMinute is >= 1 and <= 100_000, "GuestUploads:FormSubmitsPerMinute must be between 1 and 100000.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<GuestUploadOptions>>().Value);
        services.AddSingleton<IGuestUploadStore, RedisGuestUploadStore>();

        services.AddScoped<IDraftFileHolds, DraftFileHolds>();
        services.AddHostedService<AnswerFileLinkWorker>();
        services.AddHostedService<OutboxDispatcher>();

        services.AddSingleton<IMailDispatcher, OutboxMailDispatcher>();
        services.AddTransient<IOutboxHandler, MailOutboxHandler>();

        services.AddOptions<FormMailOptions>()
            .BindConfiguration(FormMailOptions.SectionName)
            .Validate(
                mail => mail.ReminderThresholdHours >= 1 && mail.ReminderScanIntervalMinutes >= 1 && mail.GuestCopyDailyLimit >= 1,
                "FormMail:ReminderThresholdHours, ReminderScanIntervalMinutes and GuestCopyDailyLimit must be at least 1.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<FormMailOptions>>().Value);
        services.AddHostedService<PendingResponseReminderWorker>();
        services.AddHostedService<FormAttemptExpiryWorker>();

        return services;
    }

    /// <summary>Sağlayıcıların bıraktığı notları ve misafir yüklemenin etkin durumunu açılışta bir kez yazar.</summary>
    public static void LogIntegrationState(this IServiceProvider services, ILogger logger)
    {
        foreach (var notice in services.GetServices<IntegrationNotice>())
            logger.Log(notice.Level, "{Notice}", notice.Message);

        var guestUploads = services.GetRequiredService<GuestUploadOptions>().Enabled;
        var humanCheck = services.GetRequiredService<IHumanVerifier>().IsEnabled;
        var fileStore = services.GetRequiredService<IAnswerFileStore>().IsConfigured;

        if (guestUploads && !humanCheck)
            logger.LogWarning("GuestUploads:Mode on ama insan doğrulaması kapalı; misafir dosya yükleme kapalı kalıyor");

        if (guestUploads && !fileStore)
            logger.LogWarning("GuestUploads:Mode on ama dosya deposu yok; misafir dosya yükleme kapalı kalıyor");

        logger.LogInformation(
            "İnsan doğrulaması {HumanCheckState}, misafir dosya yükleme {GuestUploadState}",
            humanCheck ? "on" : "off",
            guestUploads && humanCheck && fileStore ? "on" : "off");
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
