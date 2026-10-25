using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Application.Services.Mail;
using Skylab.Forms.Infrastructure.Scheduling;

namespace Skylab.Forms.Infrastructure.Mail;

public class PendingResponseReminderWorker(
    IServiceScopeFactory scopeFactory,
    ICacheService cache,
    IOptions<FormMailOptions> options,
    ILogger<PendingResponseReminderWorker> logger) : ScheduledWorker(cache, logger)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly FormMailOptions _options = options.Value;

    protected override string LockKey => "forms:pending-reminder:lock";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(_options.ReminderScanIntervalMinutes);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        string.IsNullOrEmpty(_options.PendingReminderTemplateKey) ? Task.CompletedTask : base.ExecuteAsync(stoppingToken);

    protected override async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPendingResponseReminder>().RemindAsync(ct);
    }
}
