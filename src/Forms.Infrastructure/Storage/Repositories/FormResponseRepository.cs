using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormResponseRepository : IFormResponseRepository
{
    private readonly FormsDbContext _context;

    public FormResponseRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<FormResponse?> GetLatestForUserAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && r.UserId == userId && !r.IsArchived && r.Status != FormResponseStatus.Provisional)
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<FormResponseCounts> GetCountsAsync(Guid formId, CancellationToken ct = default)
    {
        var result = await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && r.Status != FormResponseStatus.Provisional)
            .GroupBy(_ => 1)
            .Select(g => new FormResponseCounts(
                g.Count(),
                g.Count(r => r.Status == FormResponseStatus.Pending),
                g.Average(r => (double?)r.TimeSpent)
            ))
            .FirstOrDefaultAsync(ct);

        return result ?? new FormResponseCounts(0, 0, null);
    }

    public Task<bool> HasNonArchivedResponseAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        _context.Responses.AsNoTracking()
            .AnyAsync(r => r.FormId == formId && r.UserId == userId && !r.IsArchived && r.Status != FormResponseStatus.Provisional, ct);

    public Task<FormResponse?> GetByIdWithFormAndCollaboratorsAsync(Guid responseId, CancellationToken ct = default) =>
        _context.Responses.AsNoTracking()
            .Include(r => r.Form)
            .ThenInclude(f => f.Collaborators)
            .FirstOrDefaultAsync(r => r.Id == responseId, ct);

    public Task<FormResponse?> GetForEditByIdWithFormAndCollaboratorsAsync(Guid responseId, CancellationToken ct = default) =>
        _context.Responses
            .Include(r => r.Form)
            .ThenInclude(f => f.Collaborators)
            .FirstOrDefaultAsync(r => r.Id == responseId, ct);

    public async Task<PagedResponsesProjection> GetPagedAsync(Guid formId, GetResponsesRequest request, bool includeAttempts, DateTime now, CancellationToken ct = default)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var take = page * pageSize;
        var ascending = request.SortingDirection == "ascending";
        var showArchived = request.ShowArchived.GetValueOrDefault(false);
        var extendedOnly = request.Time == "extended";
        var endingSoon = request.Time == "soon";
        var soonLimit = now.AddHours(24);

        var responses = _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId)
            .Where(r => r.Status != FormResponseStatus.Provisional || !r.IsArchived);

        if (!showArchived)
            responses = responses.Where(r => !r.IsArchived);

        responses = request.ResponderType switch
        {
            FormResponderType.Registered => responses.Where(r => r.UserId != null),
            FormResponderType.Anonymous => responses.Where(r => r.UserId == null),
            _ => responses
        };

        if (request.FilterByUserId.HasValue)
            responses = responses.Where(r => r.UserId == request.FilterByUserId.Value);

        if (extendedOnly)
        {
            responses = responses.Where(r => _context.Attempts.Any(a => a.ResponseId == r.Id
                && a.Events.Any(e => e.Type == FormAttemptEventType.Extended)));
        }

        var attempts = _context.Attempts.AsNoTracking()
            .Where(a => a.FormId == formId)
            .Where(a => a.Status == FormAttemptStatus.Opened || a.Status == FormAttemptStatus.Started || a.Status == FormAttemptStatus.NoSubmission);

        if (request.FilterByUserId.HasValue)
            attempts = attempts.Where(a => a.UserId == request.FilterByUserId.Value);

        if (extendedOnly)
            attempts = attempts.Where(a => a.Events.Any(e => e.Type == FormAttemptEventType.Extended));

        if (endingSoon)
            attempts = attempts.Where(a => a.Status == FormAttemptStatus.Started && a.DeadlineAt <= soonLimit);

        var responsesAllowed = !endingSoon;
        var attemptsAllowed = includeAttempts && request.ResponderType != FormResponderType.Anonymous;

        var responseCounts = responsesAllowed
            ? await responses.GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct)
            : [];

        var attemptCounts = attemptsAllowed
            ? await attempts.GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct)
            : [];

        int ResponsesWith(FormResponseStatus status) => responseCounts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;
        int AttemptsWith(FormAttemptStatus status) => attemptCounts.FirstOrDefault(item => item.Status == status)?.Count ?? 0;

        var counts = new ResponseStatusCounts(
            ResponsesWith(FormResponseStatus.NonRestrict),
            ResponsesWith(FormResponseStatus.Pending),
            ResponsesWith(FormResponseStatus.Approved),
            ResponsesWith(FormResponseStatus.Declined),
            ResponsesWith(FormResponseStatus.Provisional),
            AttemptsWith(FormAttemptStatus.Started),
            AttemptsWith(FormAttemptStatus.Opened),
            AttemptsWith(FormAttemptStatus.NoSubmission));

        var wantResponses = responsesAllowed;
        var wantAttempts = attemptsAllowed;

        if (request.AttemptStatus is { } attemptStatus)
        {
            wantResponses = false;
            attempts = attempts.Where(a => a.Status == attemptStatus);
        }
        else if (request.Status is { } status)
        {
            wantAttempts = false;
            responses = responses.Where(r => r.Status == status);
        }

        var responseTotal = wantResponses ? await responses.CountAsync(ct) : 0;
        var attemptTotal = wantAttempts ? await attempts.CountAsync(ct) : 0;

        var averageTimeSpent = wantResponses
            ? await responses.Where(r => r.Status != FormResponseStatus.Provisional).AverageAsync(r => r.TimeSpent, ct)
            : null;

        var responseItems = wantResponses
            ? await (ascending ? responses.OrderBy(r => r.SubmittedAt) : responses.OrderByDescending(r => r.SubmittedAt))
                .Take(take)
                .Select(r => new { r.Id, r.UserId, r.Status, r.IsArchived, r.ReviewedBy, r.ArchivedBy, r.SubmittedAt, r.ReviewedAt, r.ArchivedAt, r.TimeSpent })
                .ToListAsync(ct)
            : [];

        var attemptItems = wantAttempts
            ? await (ascending ? attempts.OrderBy(a => a.UpdatedAt ?? a.CreatedAt) : attempts.OrderByDescending(a => a.UpdatedAt ?? a.CreatedAt))
                .Take(take)
                .Select(a => new
                {
                    a.Id,
                    a.UserId,
                    a.Status,
                    a.WorkflowStepId,
                    a.CreatedAt,
                    SortAt = a.UpdatedAt ?? a.CreatedAt,
                    a.StartedAt,
                    a.DeadlineAt,
                    a.ExpiredAt,
                    a.ReminderSentAt
                })
                .ToListAsync(ct)
            : [];

        var merged = responseItems
            .Select(r => (SortAt: r.SubmittedAt, ResponseId: (Guid?)r.Id, AttemptId: (Guid?)null))
            .Concat(attemptItems.Select(a => (SortAt: a.SortAt, ResponseId: (Guid?)null, AttemptId: (Guid?)a.Id)));

        var pageRows = (ascending ? merged.OrderBy(row => row.SortAt) : merged.OrderByDescending(row => row.SortAt))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pageResponseIds = pageRows.Where(row => row.ResponseId.HasValue).Select(row => row.ResponseId!.Value).ToList();
        var pageAttemptIds = pageRows.Where(row => row.AttemptId.HasValue).Select(row => row.AttemptId!.Value).ToList();

        var linkedAttempts = includeAttempts && pageResponseIds.Count > 0
            ? await _context.Attempts.AsNoTracking()
                .Where(a => a.ResponseId != null && pageResponseIds.Contains(a.ResponseId.Value))
                .Select(a => new
                {
                    a.Id,
                    ResponseId = a.ResponseId!.Value,
                    a.Status,
                    a.WorkflowStepId,
                    a.CreatedAt,
                    a.StartedAt,
                    a.DeadlineAt,
                    a.ExpiredAt,
                    a.ReminderSentAt
                })
                .ToListAsync(ct)
            : [];

        var factIds = pageAttemptIds.Concat(linkedAttempts.Select(a => a.Id)).Distinct().ToList();

        var facts = factIds.Count > 0
            ? await _context.AttemptEvents.AsNoTracking()
                .Where(e => factIds.Contains(e.AttemptId) && (e.Type == FormAttemptEventType.Extended || e.Type == FormAttemptEventType.Closed))
                .GroupBy(e => e.AttemptId)
                .Select(g => new
                {
                    AttemptId = g.Key,
                    Extended = g.Sum(e => e.Type == FormAttemptEventType.Extended ? (e.Minutes ?? 0) : 0),
                    Closed = g.Count(e => e.Type == FormAttemptEventType.Closed)
                })
                .ToListAsync(ct)
            : [];

        (int Extended, bool Closed) FactsOf(Guid attemptId)
        {
            var fact = facts.FirstOrDefault(item => item.AttemptId == attemptId);
            return fact is null ? (0, false) : (fact.Extended, fact.Closed > 0);
        }

        var items = pageRows.Select(row =>
        {
            if (row.ResponseId is { } responseId)
            {
                var r = responseItems.First(item => item.Id == responseId);
                var linked = linkedAttempts.FirstOrDefault(a => a.ResponseId == responseId);
                ResponseAttemptProjection? attempt = null;

                if (linked is not null)
                {
                    var (extended, closed) = FactsOf(linked.Id);
                    attempt = new ResponseAttemptProjection(
                        linked.Id, linked.Status, linked.WorkflowStepId, linked.CreatedAt, linked.StartedAt, linked.DeadlineAt,
                        linked.ExpiredAt, linked.ReminderSentAt, extended, closed, r.SubmittedAt);
                }

                return new ResponseRowProjection(
                    r.Id, r.UserId, r.Status, r.IsArchived, r.ReviewedBy, r.ArchivedBy, r.SubmittedAt, r.ReviewedAt, r.ArchivedAt, r.TimeSpent, attempt);
            }

            var a = attemptItems.First(item => item.Id == row.AttemptId);
            var (minutes, closedByTeam) = FactsOf(a.Id);

            return new ResponseRowProjection(
                a.Id, a.UserId, null, false, null, null, null, null, null, null,
                new ResponseAttemptProjection(
                    a.Id, a.Status, a.WorkflowStepId, a.CreatedAt, a.StartedAt, a.DeadlineAt,
                    a.ExpiredAt, a.ReminderSentAt, minutes, closedByTeam, null));
        }).ToList();

        double? averageTaskSeconds = null;

        if (includeAttempts)
        {
            var pairs = await _context.Attempts.AsNoTracking()
                .Where(a => a.FormId == formId && a.Status == FormAttemptStatus.Submitted && a.StartedAt != null && a.ResponseId != null)
                .Join(
                    _context.Responses.AsNoTracking().Where(r => !r.IsArchived),
                    a => a.ResponseId,
                    r => (Guid?)r.Id,
                    (a, r) => new { StartedAt = a.StartedAt!.Value, r.SubmittedAt })
                .ToListAsync(ct);

            if (pairs.Count > 0)
                averageTaskSeconds = pairs.Average(pair => Math.Max(0, (pair.SubmittedAt - pair.StartedAt).TotalSeconds));
        }

        return new PagedResponsesProjection(items, responseTotal + attemptTotal, averageTimeSpent, counts, averageTaskSeconds);
    }

    public async Task<IReadOnlyList<FormResponse>> GetNonArchivedByFormAsync(Guid formId, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && !r.IsArchived && r.Status != FormResponseStatus.Provisional)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<OverduePendingFormProjection>> GetOverduePendingByFormAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var formCounts = await _context.Responses.AsNoTracking()
            .Where(r => r.Status == FormResponseStatus.Pending
                && !r.IsArchived
                && r.PendingReminderSentAt == null
                && r.SubmittedAt <= cutoff)
            .GroupBy(r => r.FormId)
            .Select(g => new { FormId = g.Key, PendingCount = g.Count() })
            .ToListAsync(ct);

        if (formCounts.Count == 0) return [];

        var formIds = formCounts.Select(x => x.FormId).ToList();

        var forms = await _context.Forms.AsNoTracking()
            .Where(f => formIds.Contains(f.Id))
            .Select(f => new
            {
                f.Id,
                f.Title,
                ReviewerIds = f.Collaborators
                    .Where(c => c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor)
                    .Select(c => c.UserId)
                    .ToList()
            })
            .ToListAsync(ct);

        return formCounts
            .Join(forms, c => c.FormId, f => f.Id, (c, f) =>
                new OverduePendingFormProjection(f.Id, f.Title, c.PendingCount, f.ReviewerIds))
            .ToList();
    }

    public Task MarkOverduePendingRemindedAsync(DateTime cutoff, DateTime remindedAt, CancellationToken ct = default) =>
        _context.Responses
            .IgnoreQueryFilters()
            .Where(r => r.Status == FormResponseStatus.Pending
                && !r.IsArchived
                && r.PendingReminderSentAt == null
                && r.SubmittedAt <= cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PendingReminderSentAt, remindedAt), ct);

    public void Add(FormResponse response) => _context.Responses.Add(response);

    public void Remove(FormResponse response) => _context.Responses.Remove(response);
}
