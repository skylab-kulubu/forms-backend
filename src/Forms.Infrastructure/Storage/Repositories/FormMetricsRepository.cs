using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormMetricsRepository : IFormMetricsRepository
{
    private readonly FormsDbContext _context;

    public FormMetricsRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<FormBasicStats?> GetFormBasicStatsAsync(Guid formId, CancellationToken ct = default) =>
        _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId)
            .GroupBy(_ => 1)
            .Select(g => new FormBasicStats(
                g.Count(),
                g.Count(r => r.Status == FormResponseStatus.Pending),
                g.Count(r => r.Status == FormResponseStatus.Approved),
                g.Count(r => r.Status == FormResponseStatus.Declined),
                g.Average(r => (double?)r.TimeSpent),
                g.Count(r => r.UserId != null),
                g.Count(r => r.UserId == null)
            ))
            .FirstOrDefaultAsync(ct);

    // Analytics aggregates the jsonb Data client-side (see AnswerAnalyticsBuilder),
    // so pull the raw answer lists for every non-archived response of the form.
    public async Task<IReadOnlyList<List<FormResponseSchemaItem>>> GetNonArchivedResponseDataAsync(Guid formId, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && !r.IsArchived)
            .Select(r => r.Data)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DailyResponseCount>> GetDailyResponseCountsAsync(Guid formId, DateTime sinceDate, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && r.SubmittedAt >= sinceDate)
            .GroupBy(r => r.SubmittedAt.Date)
            .Select(g => new DailyResponseCount(g.Key, g.Count()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<HourlyResponseCount>> GetHourlyResponseCountsAsync(Guid formId, DateTime sinceTime, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && r.SubmittedAt >= sinceTime)
            .GroupBy(r => new { r.SubmittedAt.Date, r.SubmittedAt.Hour })
            .Select(g => new HourlyResponseCount(g.Key.Date, g.Key.Hour, g.Count()))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ResponseSourceCount>> GetResponseSourceCountsAsync(Guid formId, DateTime sinceTime, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && !r.IsArchived && r.SubmittedAt >= sinceTime)
            .GroupBy(r => r.Attribution!.Source)
            .Select(g => new ResponseSourceCount(g.Key, g.Count()))
            .ToListAsync(ct);

    public Task<int> GetTotalFormsCountAsync(CancellationToken ct = default) =>
        _context.Forms.AsNoTracking().CountAsync(ct);

    public Task<int> GetTotalResponsesCountAsync(CancellationToken ct = default) =>
        _context.Responses.AsNoTracking().CountAsync(ct);

    public Task<int> GetPendingNonArchivedResponsesCountAsync(CancellationToken ct = default) =>
        _context.Responses.AsNoTracking()
            .CountAsync(r => r.Status == FormResponseStatus.Pending && !r.IsArchived, ct);

    public async Task<IReadOnlyList<DateTime>> GetFormCreatedDatesAsync(DateTime sinceDate, CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking()
            .Where(f => f.CreatedAt >= sinceDate)
            .Select(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DateTime>> GetResponseSubmittedDatesAsync(DateTime sinceDate, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.SubmittedAt >= sinceDate)
            .Select(r => r.SubmittedAt)
            .ToListAsync(ct);
}
