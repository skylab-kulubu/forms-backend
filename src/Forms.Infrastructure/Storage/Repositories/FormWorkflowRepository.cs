using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
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
                node.WorkflowVersion.Workflow.Intake,
                node.WorkflowVersion.Nodes.Count,
                node.WorkflowVersion.Nodes.Where(start => start.IsStart).Select(start => start.FormId).FirstOrDefault()))
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

    public async Task<IReadOnlyDictionary<Guid, WorkflowFormMembership>> GetFormMembershipsAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default)
    {
        var empty = new Dictionary<Guid, WorkflowFormMembership>();

        if (formIds.Count == 0) return empty;

        var usages = await _context.WorkflowNodes.AsNoTracking()
            .Where(node => formIds.Contains(node.FormId))
            .Where(node => node.WorkflowVersion.Status != WorkflowStatus.Archived)
            .Where(node => node.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
            .Select(node => new
            {
                node.FormId,
                node.NodeKey,
                node.IsStart,
                node.RequiresManualReview,
                node.WorkflowVersionId,
                IsPublished = node.WorkflowVersion.Status == WorkflowStatus.Published,
                node.WorkflowVersion.WorkflowId,
                WorkflowName = node.WorkflowVersion.Workflow.Name,
                node.WorkflowVersion.Workflow.AllowMultipleRuns,
                node.WorkflowVersion.Workflow.Intake
            })
            .ToListAsync(ct);

        if (usages.Count == 0) return empty;

        // Yayınlanmış üyelik taslağı bastırır: kilitleri belirleyen odur.
        var chosen = usages
            .GroupBy(usage => usage.FormId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(usage => usage.IsPublished).First());

        var publishedVersionIds = chosen.Values
            .Where(usage => usage.IsPublished)
            .Select(usage => usage.WorkflowVersionId)
            .Distinct()
            .ToList();

        var lockedByNodeKey = await FindLockedQuestionsAsync(publishedVersionIds, ct);

        return chosen.ToDictionary(
            entry => entry.Key,
            entry => new WorkflowFormMembership(
                entry.Value.WorkflowId,
                entry.Value.WorkflowName,
                entry.Value.IsStart,
                entry.Value.IsPublished,
                entry.Value.AllowMultipleRuns,
                entry.Value.Intake,
                entry.Value.RequiresManualReview,
                entry.Value.IsPublished && lockedByNodeKey.TryGetValue(entry.Value.NodeKey, out var locked)
                    ? [.. locked.Select(question => new WorkflowLockedQuestion(question.Key, question.Value))]
                    : []));
    }

    /// <summary>
    /// Yayınlanmış sürümlerde hangi adımın hangi sorularının ve hangi değerlerinin
    /// okunduğu. Koşullar jsonb içinde saklandığı için sorguyla süzülemez;
    /// yönlendirmeler tek seferde okunup bellekte taranır.
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, HashSet<string>>>> FindLockedQuestionsAsync(
        List<Guid> versionIds,
        CancellationToken ct)
    {
        var readsByNodeKey = new Dictionary<string, Dictionary<string, HashSet<string>>>(StringComparer.OrdinalIgnoreCase);

        if (versionIds.Count == 0) return readsByNodeKey;

        var nodeKeysById = await _context.WorkflowNodes.AsNoTracking()
            .Where(node => versionIds.Contains(node.WorkflowVersionId))
            .ToDictionaryAsync(node => node.Id, node => node.NodeKey, ct);

        var transitions = await _context.WorkflowTransitions.AsNoTracking()
            .Where(transition => versionIds.Contains(transition.WorkflowVersionId))
            .Select(transition => new { transition.SourceNodeId, transition.Condition })
            .ToListAsync(ct);

        foreach (var transition in transitions)
        {
            if (transition.Condition is null) continue;

            foreach (var rule in transition.Condition.Rules)
            {
                // Boş nodeKey, kuralın kaynak adımın kendi cevabına baktığını gösterir.
                var readsFrom = string.IsNullOrWhiteSpace(rule.NodeKey)
                    ? nodeKeysById[transition.SourceNodeId]
                    : rule.NodeKey;

                if (!readsByNodeKey.TryGetValue(readsFrom, out var questions))
                    readsByNodeKey[readsFrom] = questions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

                if (!questions.TryGetValue(rule.QuestionId, out var values))
                    questions[rule.QuestionId] = values = new HashSet<string>(StringComparer.Ordinal);

                foreach (var value in TextValuesOf(rule)) values.Add(value);
            }
        }

        return readsByNodeKey;
    }

    /// <summary>Kuralın seçenek adıyla karşılaştırdığı metinler.</summary>
    private static IEnumerable<string> TextValuesOf(WorkflowConditionRule rule)
    {
        var comparesText = rule.Comparison
            is WorkflowConditionComparison.Equals
            or WorkflowConditionComparison.NotEquals
            or WorkflowConditionComparison.In
            or WorkflowConditionComparison.NotIn
            or WorkflowConditionComparison.Contains;

        if (!comparesText) yield break;

        if (!string.IsNullOrWhiteSpace(rule.Value)) yield return rule.Value;

        foreach (var value in rule.Values ?? [])
        {
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
        }
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

    public async Task<PagedResult<WorkflowSummaryProjection>> GetOwnedWorkflowsAsync(
        Guid ownerUserId,
        GetWorkflowsRequest request,
        CancellationToken ct = default)
    {
        var query = _context.Workflows.AsNoTracking()
            .Where(workflow => workflow.OwnerUserId == ownerUserId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(workflow => EF.Functions.ILike(workflow.Name, $"%{request.Search.Trim()}%"));

        var ascending = string.Equals(request.SortDirection, "ascending", StringComparison.OrdinalIgnoreCase);

        var ordered = ascending
            ? query.OrderBy(workflow => workflow.UpdatedAt ?? workflow.CreatedAt)
            : query.OrderByDescending(workflow => workflow.UpdatedAt ?? workflow.CreatedAt);

        var totalCount = await query.CountAsync(ct);

        var rows = await ordered
            .ThenBy(workflow => workflow.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(workflow => new
            {
                workflow.Id,
                workflow.Name,
                workflow.Status,
                workflow.AllowMultipleRuns,
                workflow.Intake,
                PublishedVersion = workflow.Versions
                    .Where(version => version.Status == WorkflowStatus.Published)
                    .Select(version => (int?)version.Version)
                    .FirstOrDefault(),
                PublishedNodeCount = workflow.Versions
                    .Where(version => version.Status == WorkflowStatus.Published)
                    .Select(version => version.Nodes.Count)
                    .FirstOrDefault(),
                PublishedStartFormId = workflow.Versions
                    .Where(version => version.Status == WorkflowStatus.Published)
                    .SelectMany(version => version.Nodes)
                    .Where(node => node.IsStart)
                    .Select(node => (Guid?)node.FormId)
                    .FirstOrDefault(),
                HasDraft = workflow.Versions.Any(version => version.Status == WorkflowStatus.Draft),
                DraftNodeCount = workflow.Versions
                    .Where(version => version.Status == WorkflowStatus.Draft)
                    .Select(version => version.Nodes.Count)
                    .FirstOrDefault(),
                DraftStartFormId = workflow.Versions
                    .Where(version => version.Status == WorkflowStatus.Draft)
                    .SelectMany(version => version.Nodes)
                    .Where(node => node.IsStart)
                    .Select(node => (Guid?)node.FormId)
                    .FirstOrDefault(),
                UpdatedAt = workflow.UpdatedAt ?? workflow.CreatedAt
            })
            .ToListAsync(ct);

        // Satır, yayındaki sürümü anlatır; hiç yayınlanmadıysa taslağı.
        var items = rows.Select(row => new WorkflowSummaryProjection(
            row.Id,
            row.Name,
            row.Status,
            row.AllowMultipleRuns,
            row.Intake,
            row.PublishedVersion.HasValue ? row.PublishedStartFormId : row.DraftStartFormId,
            row.PublishedVersion.HasValue ? row.PublishedNodeCount : row.DraftNodeCount,
            row.PublishedVersion,
            row.HasDraft,
            row.UpdatedAt)).ToList();

        return new PagedResult<WorkflowSummaryProjection>(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<IReadOnlyList<WorkflowCandidateForm>> GetOwnedFormsAsync(Guid ownerUserId, CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking()
            .Where(form => form.Collaborators.Any(c => c.UserId == ownerUserId && c.Role == CollaboratorRole.Owner))
            .OrderByDescending(form => form.UpdatedAt ?? form.CreatedAt)
            .Select(form => new WorkflowCandidateForm(form.Id, form.Title, form.RequiresManualReview))
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
                AllowAnonymousResponses: form.AllowAnonymousResponses,
                WorkflowOwnerIsFormOwner: form.IsOwner,
                QuestionIds: [.. form.Schema.Select(item => item.Id)]));
    }

    public async Task<IReadOnlyDictionary<Guid, WorkflowFormHeader>> GetFormHeadersAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(form => formIds.Contains(form.Id))
            .ToDictionaryAsync(form => form.Id, form => new WorkflowFormHeader(form.Title, form.RequiresManualReview), ct);

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
