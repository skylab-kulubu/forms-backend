using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Scheduling;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.Outbox;

public sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, ICacheService cache, ILogger<OutboxDispatcher> logger)
    : ScheduledWorker(cache, logger)
{
    private const int BatchSize = 50;

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

        var due = await db.Outbox
            .Where(message => message.NextAttemptAt <= now)
            .OrderBy(message => message.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var message in due)
        {
            if (!handlers.TryGetValue(message.Type, out var handler))
            {
                db.Outbox.Remove(message);
            }
            else if (message.CreatedAt < now - handler.KeepTrying)
            {
                logger.LogWarning("Outbox mesajı {MessageId} ({Type}) {Days} günde teslim edilemediği için bırakıldı", message.Id, message.Type, handler.KeepTrying.TotalDays);
                db.Outbox.Remove(message);
            }
            else if (await TryHandleAsync(handler, message, ct))
            {
                db.Outbox.Remove(message);
            }
            else
            {
                message.Attempts++;
                message.NextAttemptAt = now + RetryBackoff.After(message.Attempts);
            }

            await db.SaveChangesAsync(ct);
        }
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
