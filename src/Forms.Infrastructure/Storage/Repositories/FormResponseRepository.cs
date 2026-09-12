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
            .Where(r => r.FormId == formId && r.UserId == userId && !r.IsArchived)
            .OrderByDescending(r => r.SubmittedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<FormResponseCounts> GetCountsAsync(Guid formId, CancellationToken ct = default)
    {
        var result = await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId)
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
            .AnyAsync(r => r.FormId == formId && r.UserId == userId && !r.IsArchived, ct);

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

    public async Task<PagedResponsesProjection> GetPagedAsync(Guid formId, GetResponsesRequest request, CancellationToken ct = default)
    {
        var query = _context.Responses.AsNoTracking().Where(r => r.FormId == formId);

        bool showArchived = request.ShowArchived.GetValueOrDefault(false);
        query = showArchived ? query.Where(r => r.IsArchived) : query.Where(r => !r.IsArchived);

        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        query = request.ResponderType switch
        {
            FormResponderType.Registered => query.Where(r => r.UserId != null),
            FormResponderType.Anonymous => query.Where(r => r.UserId == null),
            _ => query
        };

        if (request.FilterByUserId.HasValue)
            query = query.Where(r => r.UserId == request.FilterByUserId.Value);

        query = request.SortingDirection == "ascending"
            ? query.OrderBy(r => r.SubmittedAt)
            : query.OrderByDescending(r => r.SubmittedAt);

        var totalCount = await query.CountAsync(ct);
        var averageTimeSpent = await query.AverageAsync(r => r.TimeSpent, ct);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(r => new ResponseSummaryProjection(
                r.Id,
                r.UserId,
                r.Status,
                r.IsArchived,
                r.ReviewedBy,
                r.ArchivedBy,
                r.SubmittedAt,
                r.ReviewedAt,
                r.ArchivedAt
            ))
            .ToListAsync(ct);

        return new PagedResponsesProjection(items, totalCount, averageTimeSpent);
    }

    public async Task<IReadOnlyList<FormResponse>> GetNonArchivedByFormAsync(Guid formId, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && !r.IsArchived)
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
}
