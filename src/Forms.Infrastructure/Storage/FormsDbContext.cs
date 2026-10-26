using Skylab.Forms.Application.Events;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.AnswerFiles;
using Skylab.Forms.Infrastructure.Outbox;
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
    public DbSet<OutboxMessage> Outbox { get; set; }
    public DbSet<AnswerFileLink> AnswerFileLinks { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FormsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        PublishResponseChanges();
        await QueueAnswerFileLinksAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Girişli cevabın dosyaları cevapla aynı işlemde core'a bağlanmak üzere sıraya girer; silinen (geçici)
    /// cevabın bağları kaldırılmak üzere işaretlenir. Misafir dosyalarını GuestUploadService kayıttan önce
    /// kendisi bağlar.
    /// </summary>
    private async Task QueueAnswerFileLinksAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var responses = ChangeTracker.Entries<FormResponse>().ToList();

        // Kayıt başarısız olup yeniden denenirse eklenen cevap hâlâ Added görünür; aynı bağ iki kez eklenmesin.
        var queued = ChangeTracker.Entries<AnswerFileLink>().Select(e => (e.Entity.ResponseId, e.Entity.MediaId)).ToHashSet();

        AnswerFileLinks.AddRange(responses
            .Where(e => e.State == EntityState.Added)
            .SelectMany(e => AnswerFileLink.ForResponse(e.Entity, now))
            .Where(link => !queued.Contains((link.ResponseId, link.MediaId)))
            .ToList());

        var removed = responses.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
        if (removed.Count == 0) return;

        var released = await AnswerFileLinks
            .Where(l => l.ResponseId != null && removed.Contains(l.ResponseId.Value) && l.State != AnswerFileLinkState.Unlinking)
            .ToListAsync(ct);

        foreach (var link in released)
        {
            link.State = AnswerFileLinkState.Unlinking;
            link.Attempts = 0;
            link.NextAttemptAt = now;
        }
    }

    private void PublishResponseChanges()
    {
        var now = DateTime.UtcNow;

        var pending = ChangeTracker.Entries<OutboxMessage>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity.Source)
            .ToHashSet();

        Outbox.AddRange(ChangeTracker.Entries<FormResponse>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified && e.Property(r => r.Status).IsModified)
            .Where(e => e.Entity.Status != FormResponseStatus.Provisional)
            .Select(e => (Response: e.Entity, Source: $"response:{e.Entity.Id}:{e.Entity.Status}"))
            .Where(change => !pending.Contains(change.Source))
            .Select(change => OutboxMessage.Of(OutboxTypes.ResponseStatusChanged, ResponseStatusChanged.Of(change.Response), now, change.Source))
            .ToList());
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
