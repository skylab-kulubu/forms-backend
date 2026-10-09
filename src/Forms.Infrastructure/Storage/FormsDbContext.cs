using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Infrastructure.ResponseNotifications;
using Microsoft.EntityFrameworkCore;

namespace Skylab.Forms.Infrastructure.Storage;

public class FormsDbContext(DbContextOptions<FormsDbContext> options) : DbContext(options)
{
    public DbSet<Form> Forms { get; set; }
    public DbSet<FormCollaborator> Collaborators { get; set; }
    public DbSet<FormResponse> Responses { get; set; }
    public DbSet<ComponentGroup> ComponentGroups { get; set; }
    public DbSet<FormWorkflow> Workflows { get; set; }
    public DbSet<FormWorkflowVersion> WorkflowVersions { get; set; }
    public DbSet<FormWorkflowNode> WorkflowNodes { get; set; }
    public DbSet<FormWorkflowTransition> WorkflowTransitions { get; set; }
    public DbSet<FormWorkflowInstance> WorkflowInstances { get; set; }
    public DbSet<FormWorkflowStep> WorkflowSteps { get; set; }
    public DbSet<FormAttempt> Attempts { get; set; }
    public DbSet<FormAttemptEvent> AttemptEvents { get; set; }
    public DbSet<ResponseNotification> ResponseNotifications { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FormsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        QueueResponseNotifications();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Yeni ya da durumu değişen her cevap core'a bildirilmek üzere sıraya girer; core etkinlik biletini
    /// buna göre yazar. Bildirim cevapla aynı işlemde yazıldığı için biri diğeri olmadan kalmaz.
    /// </summary>
    private void QueueResponseNotifications()
    {
        var now = DateTime.UtcNow;

        var notifications = ChangeTracker.Entries<FormResponse>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified && e.Property(r => r.Status).IsModified)
            .Select(e => ResponseNotification.For(e.Entity, now))
            .OfType<ResponseNotification>()
            .ToList();

        ResponseNotifications.AddRange(notifications);
    }

    private void SetTimestamps()
    {
        var entries = ChangeTracker.Entries<BaseEntity>().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            var now = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = null;
            }
            if (entry.State == EntityState.Modified)
            {
                entry.Property(x => x.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
