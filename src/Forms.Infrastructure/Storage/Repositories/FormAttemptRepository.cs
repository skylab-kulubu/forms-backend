using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormAttemptRepository : IFormAttemptRepository
{
    private readonly FormsDbContext _context;

    public FormAttemptRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<FormAttempt?> GetLatestForEditAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        _context.Attempts
            .Where(attempt => attempt.FormId == formId && attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<FormAttempt?> GetLatestAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        _context.Attempts.AsNoTracking()
            .Where(attempt => attempt.FormId == formId && attempt.UserId == userId)
            .OrderByDescending(attempt => attempt.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task<FormAttempt?> GetForEditAsync(Guid attemptId, CancellationToken ct = default) =>
        _context.Attempts
            .Include(attempt => attempt.Form)
            .ThenInclude(form => form.Collaborators)
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId, ct);

    public Task<FormAttempt?> GetWithFormAsync(Guid attemptId, CancellationToken ct = default) =>
        _context.Attempts.AsNoTracking()
            .Include(attempt => attempt.Form)
            .ThenInclude(form => form.Collaborators)
            .FirstOrDefaultAsync(attempt => attempt.Id == attemptId, ct);

    public Task<FormAttempt?> GetByResponseAsync(Guid responseId, CancellationToken ct = default) =>
        _context.Attempts.AsNoTracking()
            .FirstOrDefaultAsync(attempt => attempt.ResponseId == responseId, ct);

    public async Task<IReadOnlyList<FormAttemptEvent>> GetEventsAsync(Guid attemptId, CancellationToken ct = default) =>
        await _context.AttemptEvents.AsNoTracking()
            .Where(item => item.AttemptId == attemptId)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deadlineBefore, int take, CancellationToken ct = default) =>
        await _context.Attempts.AsNoTracking()
            .Where(attempt => attempt.Status == FormAttemptStatus.Started && attempt.DeadlineAt <= deadlineBefore)
            .Where(attempt => attempt.Form.TimeLimitMinutes != null)
            .OrderBy(attempt => attempt.DeadlineAt)
            .Select(attempt => attempt.Id)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, FormAttemptTiming>> GetTimingsByResponseAsync(Guid formId, CancellationToken ct = default)
    {
        var rows = await _context.Attempts.AsNoTracking()
            .Where(attempt => attempt.FormId == formId && attempt.ResponseId != null)
            .Select(attempt => new
            {
                ResponseId = attempt.ResponseId!.Value,
                attempt.StartedAt,
                attempt.DeadlineAt,
                Extended = attempt.Events.Where(item => item.Type == FormAttemptEventType.Extended).Sum(item => item.Minutes ?? 0)
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.ResponseId)
            .ToDictionary(group => group.Key, group =>
            {
                var row = group.First();
                return new FormAttemptTiming(row.StartedAt, row.DeadlineAt, row.Extended);
            });
    }

    public async Task<FormAttemptStatsProjection> GetStatsAsync(Guid formId, CancellationToken ct = default)
    {
        var attempts = _context.Attempts.AsNoTracking().Where(attempt => attempt.FormId == formId);

        var statuses = await attempts
            .GroupBy(attempt => attempt.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(ct);

        int CountOf(FormAttemptStatus status) => statuses.FirstOrDefault(item => item.Status == status)?.Count ?? 0;

        var started = await attempts.CountAsync(attempt => attempt.StartedAt != null, ct);

        var expiredWithDraft = await _context.AttemptEvents.AsNoTracking()
            .Where(item => item.Attempt.FormId == formId && item.Type == FormAttemptEventType.Expired)
            .Select(item => item.AttemptId)
            .Distinct()
            .CountAsync(ct);

        var durations = await attempts
            .Where(attempt => attempt.Status == FormAttemptStatus.Submitted && attempt.StartedAt != null && attempt.ResponseId != null)
            .Join(
                _context.Responses.AsNoTracking(),
                attempt => attempt.ResponseId,
                response => (Guid?)response.Id,
                (attempt, response) => new { StartedAt = attempt.StartedAt!.Value, response.SubmittedAt })
            .ToListAsync(ct);

        var extensions = await _context.AttemptEvents.AsNoTracking()
            .Where(item => item.Attempt.FormId == formId && item.Type == FormAttemptEventType.Extended)
            .Select(item => new { item.AttemptId, item.ActorUserId, Minutes = item.Minutes ?? 0 })
            .ToListAsync(ct);

        var drafts = await attempts
            .Where(attempt => attempt.DraftSnapshot != null)
            .Select(attempt => attempt.DraftSnapshot!)
            .ToListAsync(ct);

        return new FormAttemptStatsProjection(
            await attempts.CountAsync(ct),
            started,
            CountOf(FormAttemptStatus.Submitted),
            CountOf(FormAttemptStatus.Provisional),
            CountOf(FormAttemptStatus.Started),
            CountOf(FormAttemptStatus.NoSubmission),
            expiredWithDraft,
            [.. durations.Select(item => new FormAttemptDuration(item.StartedAt, item.SubmittedAt))],
            [.. extensions.Select(item => new FormAttemptExtensionFact(item.AttemptId, item.ActorUserId, item.Minutes))],
            drafts);
    }

    public void Add(FormAttempt attempt) => _context.Attempts.Add(attempt);

    public void Add(FormAttemptEvent attemptEvent) => _context.AttemptEvents.Add(attemptEvent);
}
