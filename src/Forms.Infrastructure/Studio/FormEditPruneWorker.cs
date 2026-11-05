using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Infrastructure.Scheduling;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.Studio;

public sealed class FormEditPruneWorker(IServiceScopeFactory scopeFactory, IDistributedLock locks, TimeProvider time, ILogger<FormEditPruneWorker> logger)
    : ScheduledWorker(locks, logger)
{
    protected override string LockKey => "forms:studio-edits:lock";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var before = time.GetUtcNow().UtcDateTime - StudioLimits.EditRetention;

        var removed = await db.FormEdits.IgnoreQueryFilters().Where(edit => edit.CreatedAt < before).ExecuteDeleteAsync(ct);

        if (removed > 0) logger.LogInformation("{Count} eski stüdyo değişikliği silindi", removed);
    }
}
