using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormWorkflowInstanceRepository : IFormWorkflowInstanceRepository
{
    private readonly FormsDbContext _context;

    public FormWorkflowInstanceRepository(FormsDbContext context)
    {
        _context = context;
    }

    // İzleme bilinçli olarak açık: orchestrator bu varlıklar üzerinde yazıyor.
    public Task<FormWorkflowInstance?> GetActiveByFormAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        _context.WorkflowInstances
            .Include(instance => instance.Steps)
            .Include(instance => instance.Workflow)
            .Where(instance => instance.UserId == userId && instance.Status == WorkflowInstanceStatus.Active)
            .Where(instance => _context.WorkflowNodes
                .Any(node => node.WorkflowVersionId == instance.WorkflowVersionId && node.FormId == formId))
            .FirstOrDefaultAsync(ct);

    public Task<FormWorkflowStep?> GetStepByResponseAsync(Guid responseId, CancellationToken ct = default) =>
        _context.WorkflowSteps
            .Include(step => step.WorkflowInstance)
            .ThenInclude(instance => instance.Steps)
            .FirstOrDefaultAsync(step => step.ResponseId == responseId, ct);

    public async Task<WorkflowRunSummary?> GetLastRunAsync(Guid workflowId, Guid userId, CancellationToken ct = default)
    {
        var instance = await _context.WorkflowInstances.AsNoTracking()
            .Where(candidate => candidate.WorkflowId == workflowId && candidate.UserId == userId)
            .OrderByDescending(candidate => candidate.StartedAt)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.WorkflowVersionId,
                candidate.Status,
                candidate.Outcome,
                candidate.VoidedAt,
                candidate.VoidNote,
                LastSequence = candidate.Steps.Max(step => (int?)step.Sequence) ?? 0
            })
            .FirstOrDefaultAsync(ct);

        if (instance is null) return null;

        // Sonuçlanmış başvuruda kullanıcıya gösterilecek not, son incelenen cevabınkidir.
        var review = await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.WorkflowInstanceId == instance.Id && step.Response != null && step.Response.ReviewedAt != null)
            .OrderByDescending(step => step.Response!.ReviewedAt)
            .Select(step => new { step.Response!.ReviewNote, step.Response.ReviewedAt })
            .FirstOrDefaultAsync(ct);

        return new WorkflowRunSummary(
            instance.Id,
            instance.WorkflowVersionId,
            instance.Status,
            instance.Outcome,
            instance.LastSequence,
            review?.ReviewNote,
            review?.ReviewedAt,
            instance.VoidedAt,
            instance.VoidNote);
    }

    public Task<int> CountActiveAsync(Guid workflowId, CancellationToken ct = default) =>
        _context.WorkflowInstances.AsNoTracking()
            .CountAsync(instance => instance.WorkflowId == workflowId && instance.Status == WorkflowInstanceStatus.Active, ct);

    public async Task<IReadOnlyList<WorkflowOpenStepFact>> GetOpenStepsAsync(
        IReadOnlyCollection<Guid> workflowIds,
        DateTime now,
        CancellationToken ct = default) =>
        workflowIds.Count == 0
            ? []
            : await _context.WorkflowSteps.AsNoTracking()
                .Where(step => step.CompletedAt == null && step.WorkflowInstance.Status == WorkflowInstanceStatus.Active)
                .Where(step => workflowIds.Contains(step.WorkflowInstance.WorkflowId))
                .Select(step => new WorkflowOpenStepFact(
                    step.WorkflowInstance.WorkflowId,
                    step.Node.NodeKey,
                    step.Node.FormId,
                    step.Node.RequiresManualReview,
                    step.ResponseId != null,
                    step.Round,
                    _context.Attempts.Any(attempt => attempt.WorkflowStepId == step.Id
                        && attempt.Status == FormAttemptStatus.Started
                        && attempt.DeadlineAt > now)))
                .ToListAsync(ct);

    public async Task<IReadOnlyList<WorkflowRunTally>> CountRunsAsync(IReadOnlyCollection<Guid> workflowIds, CancellationToken ct = default) =>
        workflowIds.Count == 0
            ? []
            : await _context.WorkflowInstances.AsNoTracking()
                .Where(instance => workflowIds.Contains(instance.WorkflowId))
                .GroupBy(instance => new { instance.WorkflowId, instance.Status, instance.Outcome })
                .Select(group => new WorkflowRunTally(group.Key.WorkflowId, group.Key.Status, group.Key.Outcome, group.Count()))
                .ToListAsync(ct);

    public async Task<PagedResult<WorkflowRunRow>> GetRunsAsync(
        Guid workflowId,
        GetWorkflowRunsRequest request,
        DateTime now,
        CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var finished = request.Filter == WorkflowRunFilter.Finished;
        var nodeKey = request.NodeKey?.Trim();

        var steps = _context.WorkflowSteps.AsNoTracking()
            .Where(step => step.WorkflowInstance.WorkflowId == workflowId);

        steps = finished
            ? steps
                .Where(step => step.WorkflowInstance.Status != WorkflowInstanceStatus.Active)
                .Where(step => step.Id == _context.WorkflowSteps
                    .Where(last => last.WorkflowInstanceId == step.WorkflowInstanceId)
                    .OrderByDescending(last => last.Sequence)
                    .ThenByDescending(last => last.Round)
                    .ThenByDescending(last => last.CreatedAt)
                    .Select(last => last.Id)
                    .FirstOrDefault())
            : steps.Where(step => step.CompletedAt == null && step.WorkflowInstance.Status == WorkflowInstanceStatus.Active);

        if (request.Filter == WorkflowRunFilter.AwaitingReview)
            steps = steps.Where(step => step.ResponseId != null);

        if (!string.IsNullOrEmpty(nodeKey))
            steps = steps.Where(step => step.Node.NodeKey == nodeKey);

        var totalCount = await steps.CountAsync(ct);

        var ordered = finished
            ? steps.OrderByDescending(step => step.WorkflowInstance.CompletedAt).ThenBy(step => step.Id)
            : steps.OrderBy(step => step.CreatedAt).ThenBy(step => step.Id);

        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(step => new WorkflowRunRow(
                step.WorkflowInstanceId,
                step.WorkflowInstance.UserId,
                step.WorkflowInstance.Status,
                step.WorkflowInstance.Outcome,
                step.Node.NodeKey,
                step.Node.FormId,
                step.Sequence,
                step.Round,
                step.ResponseId != null,
                step.WorkflowInstance.StartedAt,
                step.CreatedAt,
                _context.Attempts
                    .Where(attempt => attempt.WorkflowStepId == step.Id && attempt.Status == FormAttemptStatus.Started && attempt.DeadlineAt > now)
                    .Max(attempt => attempt.DeadlineAt),
                step.WorkflowInstance.CompletedAt,
                _context.WorkflowSteps
                    .Where(answered => answered.WorkflowInstanceId == step.WorkflowInstanceId && answered.ResponseId != null)
                    .OrderByDescending(answered => answered.Sequence)
                    .ThenByDescending(answered => answered.Round)
                    .Select(answered => answered.ResponseId)
                    .FirstOrDefault(),
                _context.WorkflowSteps
                    .Where(answered => answered.WorkflowInstanceId == step.WorkflowInstanceId && answered.ResponseId != null)
                    .OrderByDescending(answered => answered.Sequence)
                    .ThenByDescending(answered => answered.Round)
                    .Select(answered => (Guid?)answered.Node.FormId)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return new PagedResult<WorkflowRunRow>(items, totalCount, page, pageSize);
    }

    public Task<bool> HasOpenStepForResponseAsync(Guid responseId, CancellationToken ct = default) =>
        _context.WorkflowSteps.AsNoTracking()
            .AnyAsync(step => step.ResponseId == responseId && step.CompletedAt == null, ct);

    public async Task<IReadOnlyList<WorkflowStepAnswers>> GetAnswersAsync(Guid instanceId, CancellationToken ct = default)
    {
        // Sorgu filtrelerini yok saymak bilinçli: formu sonradan silinmiş bir adımın
        // cevabı da rota geçmişinin parçası ve koşullarda okunabilmeli.
        var rows = await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.WorkflowInstanceId == instanceId && step.Response != null)
            .Where(step => step.Response!.Status != FormResponseStatus.Returned)
            .Select(step => new { step.Node.NodeKey, step.Response!.Data })
            .ToListAsync(ct);

        return [.. rows.Select(row => new WorkflowStepAnswers(row.NodeKey, row.Data))];
    }

    public async Task<IReadOnlyList<WorkflowJourneyStepFact>> GetJourneyStepsAsync(Guid instanceId, CancellationToken ct = default) =>
        await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.WorkflowInstanceId == instanceId)
            .OrderBy(step => step.Sequence)
            .ThenBy(step => step.Round)
            .Select(step => new WorkflowJourneyStepFact(
                step.Sequence,
                step.NodeId,
                step.Node.FormId,
                step.CompletedAt == null,
                step.Response == null ? null : (FormResponseStatus?)step.Response.Status,
                step.Response == null ? null : (DateTime?)step.Response.SubmittedAt,
                step.Response == null ? null : step.Response.ReviewedAt,
                step.Response == null ? null : step.Response.ReviewNote,
                step.Round,
                step.Response == null ? null : step.Response.ReturnedQuestionIds))
            .ToListAsync(ct);

    public async Task<ResponseWorkflowProjection?> GetContextByResponseAsync(Guid responseId, CancellationToken ct = default)
    {
        var owningStep = await _context.WorkflowSteps.AsNoTracking()
            .Where(step => step.ResponseId == responseId)
            .Select(step => new { step.WorkflowInstanceId, step.Sequence, step.Round })
            .FirstOrDefaultAsync(ct);

        if (owningStep is null) return null;

        return await BuildContextAsync(owningStep.WorkflowInstanceId, owningStep.Sequence, owningStep.Round, ct);
    }

    public async Task<ResponseWorkflowProjection?> GetContextByStepAsync(Guid stepId, CancellationToken ct = default)
    {
        var step = await _context.WorkflowSteps.AsNoTracking()
            .Where(candidate => candidate.Id == stepId)
            .Select(candidate => new { candidate.WorkflowInstanceId, candidate.Sequence, candidate.Round })
            .FirstOrDefaultAsync(ct);

        return step is null ? null : await BuildContextAsync(step.WorkflowInstanceId, step.Sequence, step.Round, ct);
    }

    public Task<FormWorkflowStep?> GetStepForEditAsync(Guid stepId, CancellationToken ct = default) =>
        _context.WorkflowSteps
            .Include(step => step.WorkflowInstance)
            .ThenInclude(instance => instance.Steps)
            .Include(step => step.WorkflowInstance)
            .ThenInclude(instance => instance.Workflow)
            .FirstOrDefaultAsync(step => step.Id == stepId, ct);

    private async Task<ResponseWorkflowProjection> BuildContextAsync(Guid instanceId, int sequence, int round, CancellationToken ct)
    {
        // Formu sonradan silinmiş bir adım da başvurunun geçmişinin parçasıdır.
        var steps = await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.WorkflowInstanceId == instanceId)
            .OrderBy(step => step.Sequence)
            .ThenBy(step => step.Round)
            .Select(step => new
            {
                step.Sequence,
                step.Round,
                step.ResponseId,
                Status = step.Response == null ? (FormResponseStatus?)null : step.Response.Status,
                SubmittedAt = step.Response == null ? (DateTime?)null : step.Response.SubmittedAt,
                ReviewedAt = step.Response == null ? null : step.Response.ReviewedAt,
                step.Node.FormId
            })
            .ToListAsync(ct);

        var formIds = steps.Select(step => step.FormId).Distinct().ToList();

        var titles = await _context.Forms.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(form => formIds.Contains(form.Id))
            .ToDictionaryAsync(form => form.Id, form => form.Title, ct);

        var mapped = steps
            .Select(step => new ResponseWorkflowStepProjection(
                step.Sequence,
                titles.TryGetValue(step.FormId, out var title) ? title : "(silinmiş form)",
                step.ResponseId,
                step.Status,
                step.Round,
                step.SubmittedAt,
                step.ReviewedAt))
            .ToList();

        return new ResponseWorkflowProjection(instanceId, sequence, mapped, round);
    }

    public async Task<WorkflowRevisionFacts?> GetRevisionAsync(Guid stepId, CancellationToken ct = default)
    {
        var revision = await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.Id == stepId && step.PreviousStep != null && step.PreviousStep.NodeId == step.NodeId)
            .Where(step => step.PreviousStep!.Response != null && step.PreviousStep.Response.Status == FormResponseStatus.Returned)
            .Select(step => new
            {
                step.Round,
                step.PreviousStep!.Response!.ReviewNote,
                step.PreviousStep.Response.ReviewedAt,
                step.PreviousStep.Response.ReturnedQuestionIds,
                step.PreviousStep.Response.Data
            })
            .FirstOrDefaultAsync(ct);

        return revision is null
            ? null
            : new WorkflowRevisionFacts(revision.Round, revision.ReviewNote, revision.ReviewedAt, revision.ReturnedQuestionIds ?? [], revision.Data);
    }

    public async Task<WorkflowRunFacts?> GetRunFactsAsync(Guid instanceId, CancellationToken ct = default)
    {
        var run = await _context.WorkflowInstances.AsNoTracking()
            .Where(instance => instance.Id == instanceId)
            .Select(instance => new
            {
                instance.UserId,
                instance.WorkflowId,
                instance.WorkflowVersionId,
                instance.Workflow.OwnerUserId,
                instance.Workflow.Name,
                WorkflowStatus = instance.Workflow.Status,
                instance.Workflow.AllowMultipleRuns,
                instance.Workflow.Intake,
                instance.Workflow.IntakeClosesAt,
                instance.Status,
                instance.Outcome,
                instance.CompletedAt,
                instance.VoidedAt,
                instance.VoidedBy,
                instance.VoidNote,
                PublishedVersionId = _context.WorkflowVersions
                    .Where(version => version.WorkflowId == instance.WorkflowId && version.Status == WorkflowStatus.Published)
                    .Select(version => (Guid?)version.Id)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        if (run is null) return null;

        // Yeni başvuru yayındaki sürümle başlar; iki sürümün birinde süreli adım varsa eski deneme yeni adımı kilitler.
        var startVersionId = run.PublishedVersionId ?? run.WorkflowVersionId;
        Guid[] versionIds = startVersionId == run.WorkflowVersionId ? [run.WorkflowVersionId] : [run.WorkflowVersionId, startVersionId];

        var nodes = await _context.WorkflowNodes.AsNoTracking()
            .Where(node => versionIds.Contains(node.WorkflowVersionId))
            .Select(node => new { node.WorkflowVersionId, node.FormId, node.IsStart })
            .ToListAsync(ct);

        var formIds = nodes.Select(node => node.FormId).Distinct().ToList();

        var hasTimedStep = await _context.Forms.AsNoTracking()
            .IgnoreQueryFilters()
            .AnyAsync(form => formIds.Contains(form.Id) && form.TimeLimitMinutes > 0, ct);

        var startFormId = nodes.FirstOrDefault(node => node.WorkflowVersionId == startVersionId && node.IsStart)?.FormId
            ?? nodes.First(node => node.IsStart).FormId;

        return new WorkflowRunFacts(
            instanceId,
            run.UserId,
            run.WorkflowId,
            run.OwnerUserId,
            run.Name,
            run.WorkflowStatus,
            run.AllowMultipleRuns,
            run.Intake,
            run.IntakeClosesAt,
            run.Status,
            run.Outcome,
            run.CompletedAt,
            run.VoidedAt,
            run.VoidedBy,
            run.VoidNote,
            startFormId,
            hasTimedStep);
    }

    public Task<bool> CanManageRunAsync(Guid instanceId, Guid userId, CancellationToken ct = default) =>
        _context.WorkflowInstances.AsNoTracking()
            .Where(instance => instance.Id == instanceId)
            .AnyAsync(instance => instance.Workflow.OwnerUserId == userId
                || _context.Collaborators.Any(collaborator => collaborator.UserId == userId
                    && (collaborator.Role == CollaboratorRole.Owner || collaborator.Role == CollaboratorRole.Editor)
                    && instance.Steps.Any(step => step.Node.FormId == collaborator.FormId)), ct);

    public Task<FormWorkflowInstance?> GetForVoidAsync(Guid instanceId, CancellationToken ct = default) =>
        _context.WorkflowInstances
            .Include(instance => instance.Steps)
            .Include(instance => instance.Workflow)
            .FirstOrDefaultAsync(instance => instance.Id == instanceId, ct);

    public void Add(FormWorkflowInstance instance) => _context.WorkflowInstances.Add(instance);

    public void Add(FormWorkflowStep step) => _context.WorkflowSteps.Add(step);
}
