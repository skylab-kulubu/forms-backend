using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
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
            .Select(candidate => new { candidate.Id, candidate.Status, candidate.Outcome })
            .FirstOrDefaultAsync(ct);

        if (instance is null) return null;

        // Sonuçlanmış başvuruda kullanıcıya gösterilecek not, son incelenen cevabınkidir.
        var review = await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.WorkflowInstanceId == instance.Id && step.Response != null && step.Response.ReviewedAt != null)
            .OrderByDescending(step => step.Response!.ReviewedAt)
            .Select(step => new { step.Response!.ReviewNote, step.Response.ReviewedAt })
            .FirstOrDefaultAsync(ct);

        return new WorkflowRunSummary(instance.Id, instance.Status, instance.Outcome, review?.ReviewNote, review?.ReviewedAt);
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
            .Select(step => new { step.Node.NodeKey, step.Response!.Data })
            .ToListAsync(ct);

        return [.. rows.Select(row => new WorkflowStepAnswers(row.NodeKey, row.Data))];
    }

    public async Task<ResponseWorkflowProjection?> GetContextByResponseAsync(Guid responseId, CancellationToken ct = default)
    {
        var owningStep = await _context.WorkflowSteps.AsNoTracking()
            .Where(step => step.ResponseId == responseId)
            .Select(step => new { step.WorkflowInstanceId, step.Sequence })
            .FirstOrDefaultAsync(ct);

        if (owningStep is null) return null;

        // Formu sonradan silinmiş bir adım da başvurunun geçmişinin parçasıdır.
        var steps = await _context.WorkflowSteps.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(step => step.WorkflowInstanceId == owningStep.WorkflowInstanceId)
            .OrderBy(step => step.Sequence)
            .Select(step => new
            {
                step.Sequence,
                step.ResponseId,
                Status = step.Response == null ? (FormResponseStatus?)null : step.Response.Status,
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
                step.Status))
            .ToList();

        return new ResponseWorkflowProjection(owningStep.WorkflowInstanceId, owningStep.Sequence, mapped);
    }

    public void Add(FormWorkflowInstance instance) => _context.WorkflowInstances.Add(instance);

    public void Add(FormWorkflowStep step) => _context.WorkflowSteps.Add(step);
}
