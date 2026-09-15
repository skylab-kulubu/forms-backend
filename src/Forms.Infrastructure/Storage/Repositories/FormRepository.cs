using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormRepository : IFormRepository
{
    private readonly FormsDbContext _context;

    public FormRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<Form?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Form?> GetWithCollaboratorsAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking()
            .Include(f => f.Collaborators)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Form?> GetWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking()
            .Include(f => f.Collaborators)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<bool> IsUserCollaboratorAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        _context.Collaborators.AsNoTracking()
            .AnyAsync(c => c.FormId == formId && c.UserId == userId && c.Role != CollaboratorRole.None, ct);

    public Task<bool> ExistsAsync(Guid formId, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking().AnyAsync(f => f.Id == formId, ct);

    public Task<bool> IsFormOpenAsync(Guid formId, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking().AnyAsync(f => f.Id == formId && f.Status == FormStatus.Open, ct);

    public Task<Form?> GetForEditWithCollaboratorsAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms
            .Include(f => f.Collaborators)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Form?> GetForEditWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms
            .Include(f => f.Collaborators)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<Form?> GetForEditOwnedByAsync(Guid id, Guid ownerId, CancellationToken ct = default) =>
        _context.Forms
            .Where(f => f.Id == id)
            .Where(f => f.Collaborators.Any(c => c.UserId == ownerId && c.Role == CollaboratorRole.Owner))
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<FormSummaryContract>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken ct = default)
    {
        var query = _context.Forms.AsNoTracking()
            .Where(f => f.Status != FormStatus.Deleted);

        query = request.Role.HasValue
            ? query.Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == request.Role.Value))
            : query.Where(f => f.Collaborators.Any(c => c.UserId == userId));

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(f => EF.Functions.ILike(f.Title, $"%{request.Search.Trim()}%"));

        if (request.AllowAnonymous.HasValue)
            query = query.Where(f => f.AllowAnonymousResponses == request.AllowAnonymous.Value);

        if (request.AllowMultiple.HasValue)
            query = query.Where(f => f.AllowMultipleResponses == request.AllowMultiple.Value);

        if (request.RequiresManualReview.HasValue)
            query = query.Where(f => f.RequiresManualReview == request.RequiresManualReview.Value);

        query = ApplyUserFormsSorting(query, request.SortBy, request.SortDirection, userId);

        var totalCount = await query.CountAsync(ct);

        var forms = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(f => new FormSummaryContract(
                f.Id,
                f.Title,
                f.Status,
                f.Collaborators.FirstOrDefault(c => c.UserId == userId)!.Role,
                f.AllowAnonymousResponses,
                f.AllowMultipleResponses,
                f.RequiresManualReview,
                null,
                f.UpdatedAt ?? f.CreatedAt,
                f.Responses.Count()
            ))
            .ToListAsync(ct);

        return new PagedResult<FormSummaryContract>(forms, totalCount, request.Page, request.PageSize);
    }

    private static IQueryable<Form> ApplyUserFormsSorting(IQueryable<Form> query, string? sortBy, string? sortDirection, Guid userId)
    {
        var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Form> ordered = sortBy?.Trim().ToLowerInvariant() switch
        {
            "status" => ascending
                ? query.OrderBy(f => f.Status)
                : query.OrderByDescending(f => f.Status),
            "responsecount" => ascending
                ? query.OrderBy(f => f.Responses.Count())
                : query.OrderByDescending(f => f.Responses.Count()),
            "userrole" => ascending
                ? query.OrderBy(f => f.Collaborators.Where(c => c.UserId == userId).Select(c => c.Role).FirstOrDefault())
                : query.OrderByDescending(f => f.Collaborators.Where(c => c.UserId == userId).Select(c => c.Role).FirstOrDefault()),
            _ => ascending
                ? query.OrderBy(f => f.UpdatedAt ?? f.CreatedAt)
                : query.OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt),
        };

        return ordered.ThenBy(f => f.Id);
    }

    public async Task<PagedResult<FormAllSummaryProjection>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken ct = default)
    {
        var query = _context.Forms.AsNoTracking()
            .Where(f => f.Status != FormStatus.Deleted);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(f => EF.Functions.ILike(f.Title, $"%{request.Search.Trim()}%"));

        if (request.AllowAnonymous.HasValue)
            query = query.Where(f => f.AllowAnonymousResponses == request.AllowAnonymous.Value);

        if (request.AllowMultiple.HasValue)
            query = query.Where(f => f.AllowMultipleResponses == request.AllowMultiple.Value);

        if (request.RequiresManualReview.HasValue)
            query = query.Where(f => f.RequiresManualReview == request.RequiresManualReview.Value);

        query = request.SortDirection?.ToLower() == "ascending"
            ? query.OrderBy(f => f.UpdatedAt ?? f.CreatedAt)
            : query.OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(f => new FormAllSummaryProjection(
                f.Id,
                f.Title,
                f.Status,
                f.Collaborators.Where(c => c.Role == CollaboratorRole.Owner).Select(c => c.UserId).FirstOrDefault(),
                f.AllowAnonymousResponses,
                f.AllowMultipleResponses,
                f.RequiresManualReview,
                f.CreatedAt,
                f.UpdatedAt,
                f.Responses.Count()
            ))
            .ToListAsync(ct);

        return new PagedResult<FormAllSummaryProjection>(items, totalCount, request.Page, request.PageSize);
    }

    public void Add(Form form) => _context.Forms.Add(form);
}
