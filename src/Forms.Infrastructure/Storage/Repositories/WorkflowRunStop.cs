using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

/// <summary>
/// Aktif başvuruları FormWorkflowInstance.Stop'un toplu karşılığıyla sonuçsuz bitirir ve açık adımlarını kapatır. Hesap
/// silme ve arşivleme bunu kullanır; WorkflowArchiveTests ikisinin aynı durumu yazdığını denetler.
/// </summary>
internal static class WorkflowRunStop
{
    public static async Task<int> StopAsync(FormsDbContext context, IQueryable<FormWorkflowInstance> runs, DateTime now, CancellationToken ct)
    {
        var active = runs.Where(run => run.Status == WorkflowInstanceStatus.Active);

        // Adımlar önce kapanır: başvuru Terminated olunca bu sorgu onu bulamaz.
        await context.WorkflowSteps.IgnoreQueryFilters()
            .Where(step => step.CompletedAt == null && active.Any(run => run.Id == step.WorkflowInstanceId))
            .ExecuteUpdateAsync(s => s.SetProperty(step => step.CompletedAt, now).SetProperty(step => step.UpdatedAt, now), ct);

        return await active.ExecuteUpdateAsync(s => s
            .SetProperty(run => run.Status, WorkflowInstanceStatus.Terminated)
            .SetProperty(run => run.CompletedAt, now)
            .SetProperty(run => run.UpdatedAt, now), ct);
    }
}
