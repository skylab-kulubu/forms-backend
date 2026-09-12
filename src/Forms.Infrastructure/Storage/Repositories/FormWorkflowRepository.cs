using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormWorkflowRepository : IFormWorkflowRepository
{
    private readonly FormsDbContext _context;

    public FormWorkflowRepository(FormsDbContext context)
    {
        _context = context;
    }

    // Bir formun yayındaki tek bir akışta yer alması publish sırasında güvence
    // altına alınır; burada tekil sonuç beklenir.
    public Task<WorkflowNodeLocation?> FindPublishedNodeAsync(Guid formId, CancellationToken ct = default) =>
        _context.WorkflowNodes.AsNoTracking()
            .Where(node => node.FormId == formId)
            .Where(node => node.WorkflowVersion.Status == WorkflowStatus.Published)
            .Where(node => node.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
            .Select(node => new WorkflowNodeLocation(
                node.WorkflowVersion.WorkflowId,
                node.WorkflowVersionId,
                node.Id,
                node.NodeKey,
                node.IsStart,
                node.WorkflowVersion.Workflow.AllowMultipleRuns,
                node.WorkflowVersion.Nodes.Count))
            .FirstOrDefaultAsync(ct);

    public async Task<WorkflowDefinition?> GetDefinitionAsync(Guid workflowVersionId, CancellationToken ct = default)
    {
        var version = await _context.WorkflowVersions.AsNoTracking()
            .Include(candidate => candidate.Nodes)
            .Include(candidate => candidate.Transitions)
            .FirstOrDefaultAsync(candidate => candidate.Id == workflowVersionId, ct);

        return version is null
            ? null
            : new WorkflowDefinition(version.WorkflowId, version.Id, [.. version.Nodes], [.. version.Transitions]);
    }

    public async Task<WorkflowFormLock?> GetPublishedLockAsync(Guid formId, CancellationToken ct = default)
    {
        var usages = await _context.WorkflowNodes.AsNoTracking()
            .Where(node => node.FormId == formId)
            .Where(node => node.WorkflowVersion.Status == WorkflowStatus.Published)
            .Where(node => node.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
            .Select(node => new
            {
                node.WorkflowVersionId,
                node.NodeKey,
                WorkflowName = node.WorkflowVersion.Workflow.Name
            })
            .ToListAsync(ct);

        if (usages.Count == 0) return null;

        var versionIds = usages.Select(usage => usage.WorkflowVersionId).ToList();

        var nodeKeysById = await _context.WorkflowNodes.AsNoTracking()
            .Where(node => versionIds.Contains(node.WorkflowVersionId))
            .ToDictionaryAsync(node => node.Id, node => node.NodeKey, ct);

        // Koşullar jsonb içinde saklandığı için sorguyla süzülemez; ilgili
        // version'ların yönlendirmeleri okunup bellekte taranır.
        var transitions = await _context.WorkflowTransitions.AsNoTracking()
            .Where(transition => versionIds.Contains(transition.WorkflowVersionId))
            .ToListAsync(ct);

        var formNodeKeys = usages.Select(usage => usage.NodeKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lockedQuestionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transition in transitions)
        {
            if (transition.Condition is null) continue;

            foreach (var rule in transition.Condition.Rules)
            {
                // Boş nodeKey, kuralın kaynak adımın kendi cevabına baktığını gösterir.
                var readsFrom = string.IsNullOrWhiteSpace(rule.NodeKey)
                    ? nodeKeysById[transition.SourceNodeId]
                    : rule.NodeKey;

                if (formNodeKeys.Contains(readsFrom)) lockedQuestionIds.Add(rule.QuestionId);
            }
        }

        return new WorkflowFormLock(usages[0].WorkflowName, lockedQuestionIds);
    }

    public Task<FormWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default) =>
        _context.Workflows.AsNoTracking().FirstOrDefaultAsync(workflow => workflow.Id == workflowId, ct);

    public Task<FormWorkflow?> GetForEditAsync(Guid workflowId, CancellationToken ct = default) =>
        _context.Workflows.FirstOrDefaultAsync(workflow => workflow.Id == workflowId, ct);

    public Task<FormWorkflowVersion?> GetVersionAsync(Guid workflowId, WorkflowStatus status, CancellationToken ct = default) =>
        VersionQuery(_context.WorkflowVersions.AsNoTracking(), workflowId, status).FirstOrDefaultAsync(ct);

    public Task<FormWorkflowVersion?> GetVersionForEditAsync(Guid workflowId, WorkflowStatus status, CancellationToken ct = default) =>
        VersionQuery(_context.WorkflowVersions, workflowId, status).FirstOrDefaultAsync(ct);

    private static IQueryable<FormWorkflowVersion> VersionQuery(IQueryable<FormWorkflowVersion> source, Guid workflowId, WorkflowStatus status) =>
        source
            .Include(version => version.Nodes)
            .Include(version => version.Transitions)
            .Where(version => version.WorkflowId == workflowId && version.Status == status);

    public async Task<int> GetNextVersionNumberAsync(Guid workflowId, CancellationToken ct = default)
    {
        var highest = await _context.WorkflowVersions.AsNoTracking()
            .Where(version => version.WorkflowId == workflowId)
            .MaxAsync(version => (int?)version.Version, ct);

        return (highest ?? 0) + 1;
    }

    public async Task<IReadOnlyList<WorkflowVersionProjection>> GetVersionsAsync(Guid workflowId, CancellationToken ct = default) =>
        await _context.WorkflowVersions.AsNoTracking()
            .Where(version => version.WorkflowId == workflowId)
            .OrderByDescending(version => version.Version)
            .Select(version => new WorkflowVersionProjection(
                version.Id,
                version.Version,
                version.Status,
                version.PublishedAt,
                version.Nodes.Count))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowSummaryProjection>> GetOwnedWorkflowsAsync(Guid ownerUserId, CancellationToken ct = default) =>
        await _context.Workflows.AsNoTracking()
            .Where(workflow => workflow.OwnerUserId == ownerUserId)
            .OrderByDescending(workflow => workflow.UpdatedAt ?? workflow.CreatedAt)
            .Select(workflow => new WorkflowSummaryProjection(
                workflow.Id,
                workflow.Name,
                workflow.Status,
                workflow.AllowMultipleRuns,
                workflow.Versions
                    .Where(version => version.Status == WorkflowStatus.Published)
                    .Select(version => version.Nodes.Count)
                    .FirstOrDefault(),
                workflow.UpdatedAt ?? workflow.CreatedAt))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, WorkflowNodeForm>> GetNodeFormsAsync(
        IReadOnlyCollection<Guid> formIds,
        Guid ownerUserId,
        CancellationToken ct = default)
    {
        // Silinmiş formu "yok" saymak yerine ayırt edebilmek icin filtre yok sayılır.
        var forms = await _context.Forms.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(form => formIds.Contains(form.Id))
            .Select(form => new
            {
                form.Id,
                form.Status,
                form.RequiresManualReview,
                form.AllowAnonymousResponses,
                form.Schema,
                IsOwner = form.Collaborators.Any(c => c.UserId == ownerUserId && c.Role == CollaboratorRole.Owner)
            })
            .ToListAsync(ct);

        return forms.ToDictionary(
            form => form.Id,
            form => new WorkflowNodeForm(
                Exists: form.Status != FormStatus.Deleted,
                IsOpen: form.Status == FormStatus.Open,
                RequiresManualReview: form.RequiresManualReview,
                AllowAnonymousResponses: form.AllowAnonymousResponses,
                WorkflowOwnerIsFormOwner: form.IsOwner,
                QuestionIds: [.. form.Schema.Select(item => item.Id)]));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetFormTitlesAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(form => formIds.Contains(form.Id))
            .ToDictionaryAsync(form => form.Id, form => form.Title, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> FindFormsInOtherPublishedWorkflowsAsync(
        Guid workflowId,
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default)
    {
        var clashes = await _context.WorkflowNodes.AsNoTracking()
            .Where(node => formIds.Contains(node.FormId))
            .Where(node => node.WorkflowVersion.WorkflowId != workflowId)
            .Where(node => node.WorkflowVersion.Status == WorkflowStatus.Published)
            .Where(node => node.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
            .Select(node => new { node.FormId, node.WorkflowVersion.Workflow.Name })
            .ToListAsync(ct);

        return clashes
            .GroupBy(clash => clash.FormId)
            .ToDictionary(group => group.Key, group => group.First().Name);
    }

    public async Task<IReadOnlyCollection<Guid>> FindLegacyLinkedFormsAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking()
            .Where(form => formIds.Contains(form.Id))
            .Where(form => form.LinkedFormId != null || _context.Forms.Any(parent => parent.LinkedFormId == form.Id))
            .Select(form => form.Id)
            .ToListAsync(ct);

    public void Add(FormWorkflow workflow) => _context.Workflows.Add(workflow);

    public void Add(FormWorkflowVersion version) => _context.WorkflowVersions.Add(version);

    public void AddRange(IEnumerable<FormWorkflowNode> nodes) => _context.WorkflowNodes.AddRange(nodes);

    public void AddRange(IEnumerable<FormWorkflowTransition> transitions) => _context.WorkflowTransitions.AddRange(transitions);

    public void RemoveRange(IEnumerable<FormWorkflowNode> nodes) => _context.WorkflowNodes.RemoveRange(nodes);

    public void RemoveRange(IEnumerable<FormWorkflowTransition> transitions) => _context.WorkflowTransitions.RemoveRange(transitions);
}
