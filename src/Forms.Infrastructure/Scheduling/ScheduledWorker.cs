using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Scheduling;

public abstract class ScheduledWorker(ICacheService cache, ILogger logger) : BackgroundService
{
    protected abstract string LockKey { get; }

    protected abstract TimeSpan Interval { get; }

    protected virtual TimeSpan LockTtl => Interval;

    protected virtual bool ReleasesLock => false;

    protected abstract Task RunAsync(CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                if (!await cache.AcquireLockAsync(LockKey, LockTtl, stoppingToken)) continue;

                try
                {
                    await RunAsync(stoppingToken);
                }
                finally
                {
                    if (ReleasesLock) await cache.ReleaseLockAsync(LockKey, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Worker} turu başarısız", GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
