using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Enums;

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
                node.WorkflowVersion.Workflow.AllowMultipleRuns))
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
}
