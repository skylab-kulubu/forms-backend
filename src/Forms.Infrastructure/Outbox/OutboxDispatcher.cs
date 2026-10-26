using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Scheduling;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.Outbox;

public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, IDistributedLock locks, ILogger<OutboxDispatcher> logger)
    : ScheduledWorker(locks, logger)
{
    private const int BatchSize = 50;

    private static readonly TimeSpan UnhandledKeepTrying = TimeSpan.FromDays(7);

    protected override string LockKey => "forms:outbox:lock";

    protected override TimeSpan Interval => TimeSpan.FromSeconds(10);

    protected override TimeSpan LockTtl => TimeSpan.FromMinutes(15);

    protected override bool ReleasesLock => true;

    protected override Task RunAsync(CancellationToken ct) => DispatchDueAsync(ct);

    internal async Task DispatchDueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var handlers = scope.ServiceProvider.GetServices<IOutboxHandler>().ToDictionary(handler => handler.Type);
        var now = DateTime.UtcNow;
        var dropped = new Dictionary<string, int>();

        var due = await db.Outbox
            .Where(message => message.NextAttemptAt <= now)
            .OrderBy(message => message.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in due)
        {
            ct.ThrowIfCancellationRequested();

            var handler = handlers.GetValueOrDefault(message.Type);

            if (message.CreatedAt < now - (handler?.KeepTrying ?? UnhandledKeepTrying))
            {
                db.Outbox.Remove(message);
                dropped[message.Type] = dropped.GetValueOrDefault(message.Type) + 1;
            }
            else if (handler is not null && await TryHandleAsync(handler, message, ct))
            {
                db.Outbox.Remove(message);
            }
            else
            {
                message.Attempts++;
                message.NextAttemptAt = now + RetryBackoff.After(message.Attempts);
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }

        foreach (var (type, count) in dropped)
            logger.LogWarning("{Count} outbox mesajı ({Type}) vazgeçme süresi içinde teslim edilemediği için bırakıldı", count, type);
    }

    private async Task<bool> TryHandleAsync(IOutboxHandler handler, OutboxMessage message, CancellationToken ct)
    {
        try
        {
            return await handler.HandleAsync(message.Payload, ct);
        }
        catch (JsonException)
        {
            logger.LogError("Outbox mesajı {MessageId} ({Type}) okunamadı; bırakılıyor", message.Id, message.Type);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Outbox mesajı {MessageId} ({Type}) teslim edilemedi ({Error}); yeniden denenecek", message.Id, message.Type, ex.GetType().Name);
            return false;
        }
    }
}
