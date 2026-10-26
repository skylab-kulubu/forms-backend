using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Scheduling;

/// <summary>
/// Zamanlayıcıyla dönen arka plan işi. Birden çok örnekte bir turu yalnız kilidi alan çalıştırır; bir turun hatası
/// loglanır ve sonraki tur yeniden dener.
/// </summary>
public abstract class ScheduledWorker(IDistributedLock locks, ILogger logger) : BackgroundService
{
    protected abstract string LockKey { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// Varsayılan: kilit aralık boyunca kalır, iş örnek sayısından bağımsız olarak aralıkta bir kez çalışır. Kuyruk
    /// boşaltan iş turundan uzun bir süre verip <see cref="ReleasesLock"/> ile kilidi tur bitince bırakır.
    /// </summary>
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
                if (await locks.TryAcquireAsync(LockKey, LockTtl, stoppingToken) is not { } lease) continue;

                try
                {
                    await RunAsync(stoppingToken);
                }
                finally
                {
                    if (ReleasesLock) await locks.ReleaseAsync(lease, CancellationToken.None);
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
