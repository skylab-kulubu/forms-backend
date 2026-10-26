using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Infrastructure.Scheduling;

namespace Skylab.Forms.Infrastructure.Attempts;

public class FormAttemptExpiryWorker(IServiceScopeFactory scopeFactory, IDistributedLock locks, TimeProvider time, ILogger<FormAttemptExpiryWorker> logger)
    : ScheduledWorker(locks, logger)
{
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<FormAttemptExpiryWorker> _logger = logger;

    protected override string LockKey => "forms:attempt-expiry:lock";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> due;

        using (var scope = _scopeFactory.CreateScope())
        {
            var attempts = scope.ServiceProvider.GetRequiredService<IFormAttemptService>();
            due = await attempts.GetDueAsync(time.GetUtcNow().UtcDateTime, BatchSize, ct);
        }

        foreach (var attemptId in due)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var attempts = scope.ServiceProvider.GetRequiredService<IFormAttemptService>();
                await attempts.ExpireByIdAsync(attemptId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Deneme {AttemptId} sonlandırılamadı", attemptId);
            }
        }
    }
}
