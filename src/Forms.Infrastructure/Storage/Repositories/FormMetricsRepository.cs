using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Entities;
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

    public async Task<IReadOnlyList<ResponseFact>> GetResponseFactsAsync(Guid formId, CancellationToken ct = default) =>
        await Counted(formId)
            .Select(r => new ResponseFact(r.Id, r.UserId, r.Status, r.SubmittedAt, r.TimeSpent, r.ReviewedAt, r.ReviewedBy, r.Attribution!.Source, r.Attribution!.Campaign))
            .ToListAsync(ct);

    // Analytics aggregates the jsonb Data client-side (see AnswerAnalyticsBuilder),
    // so pull the raw answer lists for every non-archived response of the form.
    public async Task<IReadOnlyList<List<FormResponseSchemaItem>>> GetNonArchivedResponseDataAsync(Guid formId, CancellationToken ct = default) =>
        await Counted(formId)
            .Select(r => r.Data)
            .ToListAsync(ct);

    private IQueryable<FormResponse> Counted(Guid formId) =>
        _context.Responses.AsNoTracking()
            .Where(r => r.FormId == formId && !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status));

    public Task<int> GetTotalFormsCountAsync(CancellationToken ct = default) =>
        _context.Forms.AsNoTracking().CountAsync(ct);

    public Task<int> GetTotalResponsesCountAsync(CancellationToken ct = default) =>
        _context.Responses.AsNoTracking().CountAsync(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status), ct);

    public Task<int> GetPendingNonArchivedResponsesCountAsync(CancellationToken ct = default) =>
        _context.Responses.AsNoTracking()
            .CountAsync(r => (r.Status == FormResponseStatus.Pending || r.Status == FormResponseStatus.Flagged) && !r.IsArchived, ct);

    public async Task<IReadOnlyList<DateTime>> GetFormCreatedDatesAsync(DateTime sinceDate, CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking()
            .Where(f => f.CreatedAt >= sinceDate)
            .Select(f => f.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DateTime>> GetResponseSubmittedDatesAsync(DateTime sinceDate, CancellationToken ct = default) =>
        await _context.Responses.AsNoTracking()
            .Where(r => r.SubmittedAt >= sinceDate && !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status))
            .Select(r => r.SubmittedAt)
            .ToListAsync(ct);
}
