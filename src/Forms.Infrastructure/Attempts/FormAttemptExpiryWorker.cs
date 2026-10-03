using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Services.Attempts;

namespace Skylab.Forms.Infrastructure.Attempts;

public class FormAttemptExpiryWorker : BackgroundService
{
    private const string LockKey = "forms:attempt-expiry:lock";
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly ILogger<FormAttemptExpiryWorker> _logger;

    public FormAttemptExpiryWorker(IServiceScopeFactory scopeFactory, ICacheService cache, ILogger<FormAttemptExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                if (await _cache.AcquireLockAsync(LockKey, Interval, stoppingToken))
                    await ExpireDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Süresi dolan denemelerin taraması başarısız");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExpireDueAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> due;

        using (var scope = _scopeFactory.CreateScope())
        {
            var attempts = scope.ServiceProvider.GetRequiredService<IFormAttemptService>();
            due = await attempts.GetDueAsync(DateTime.UtcNow, BatchSize, ct);
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
