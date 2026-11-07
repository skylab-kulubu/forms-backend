using System.Linq.Expressions;
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

    public Task<string?> GetTitleAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => (string?)f.Title)
            .FirstOrDefaultAsync(ct);

    public Task<Form?> GetWithCollaboratorsAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking()
            .Include(f => f.Collaborators)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public async Task<CollaboratorRole> GetRoleAsync(Guid formId, Guid userId, CancellationToken ct = default) =>
        await _context.Collaborators.AsNoTracking()
            .Where(c => c.FormId == formId && c.UserId == userId)
            .Select(c => (CollaboratorRole?)c.Role)
            .FirstOrDefaultAsync(ct) ?? CollaboratorRole.None;

    public Task<bool> ExistsAsync(Guid formId, CancellationToken ct = default) =>
        _context.Forms.AsNoTracking().AnyAsync(f => f.Id == formId, ct);

    public Task<Form?> GetForEditWithCollaboratorsAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms
            .Include(f => f.Collaborators)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

    public Task<List<Form>> GetForEditOwnedWithCollaboratorsAsync(IReadOnlyCollection<Guid> ids, Guid ownerId, CancellationToken ct = default) =>
        _context.Forms
            .Include(f => f.Collaborators)
            .Where(f => ids.Contains(f.Id))
            .Where(f => f.Collaborators.Any(c => c.UserId == ownerId && c.Role == CollaboratorRole.Owner))
            .ToListAsync(ct);

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

        query = ApplyWorkflowAwareFilters(query, request.AllowMultiple, request.RequiresManualReview);

        query = ApplyUserFormsSorting(query, request.SortBy, request.SortDirection, userId);

        var totalCount = await query.CountAsync(ct);

        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        var forms = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                f.Responses.Count(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status)),
                f.PublishedVersion
            ))
            .ToListAsync(ct);

        return new PagedResult<FormSummaryContract>(forms, totalCount, page, pageSize);
    }

    private IQueryable<Form> ApplyUserFormsSorting(IQueryable<Form> query, string? sortBy, string? sortDirection, Guid userId)
    {
        var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Form> ordered = sortBy?.Trim().ToLowerInvariant() switch
        {
            "status" => ascending
                ? query.OrderBy(f => f.Status)
                : query.OrderByDescending(f => f.Status),
            "responsecount" => ascending
                ? query.OrderBy(f => f.Responses.Count(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status)))
                : query.OrderByDescending(f => f.Responses.Count(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status))),
            "userrole" => ascending
                ? query.OrderBy(f => f.Collaborators.Where(c => c.UserId == userId).Select(c => c.Role).FirstOrDefault())
                : query.OrderByDescending(f => f.Collaborators.Where(c => c.UserId == userId).Select(c => c.Role).FirstOrDefault()),
            // Listedeki "Akış" kolonuyla aynı seçim: yayındaki üyelik taslağı bastırır,
            // arşivlenmişler sayılmaz. Akışta olmayan formlar null olarak sona düşer.
            "workflow" => ascending
                ? query.OrderBy(f => _context.WorkflowNodes
                    .Where(n => n.FormId == f.Id)
                    .Where(n => n.WorkflowVersion.Status != WorkflowStatus.Archived)
                    .Where(n => n.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
                    .OrderByDescending(n => n.WorkflowVersion.Status == WorkflowStatus.Published)
                    .Select(n => n.WorkflowVersion.Workflow.Name)
                    .FirstOrDefault())
                : query.OrderByDescending(f => _context.WorkflowNodes
                    .Where(n => n.FormId == f.Id)
                    .Where(n => n.WorkflowVersion.Status != WorkflowStatus.Archived)
                    .Where(n => n.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived)
                    .OrderByDescending(n => n.WorkflowVersion.Status == WorkflowStatus.Published)
                    .Select(n => n.WorkflowVersion.Workflow.Name)
                    .FirstOrDefault()),
            _ => ascending
                ? query.OrderBy(f => f.UpdatedAt ?? f.CreatedAt)
                : query.OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt),
        };

        return ordered.ThenBy(f => f.Id);
    }

    /// <summary>
    /// Yayındaki bir akışın adımı olan formda geçerli olan, formun kendi ayarı değil
    /// akışın tekrar ayarı ve adımın onay ayarıdır; süzme de onlara bakar. Yalnız
    /// taslakta yer alan form hâlâ tek başına çalıştığı için kendi ayarıyla süzülür.
    /// </summary>
    private IQueryable<Form> ApplyWorkflowAwareFilters(IQueryable<Form> query, bool? allowMultiple, bool? requiresManualReview)
    {
        var liveNodes = _context.WorkflowNodes
            .Where(n => n.WorkflowVersion.Status == WorkflowStatus.Published)
            .Where(n => n.WorkflowVersion.Workflow.Status != WorkflowStatus.Archived);

        if (allowMultiple is { } multiple)
        {
            query = query.Where(f =>
                liveNodes.Any(n => n.FormId == f.Id && n.WorkflowVersion.Workflow.AllowMultipleRuns == multiple)
                || (!liveNodes.Any(n => n.FormId == f.Id) && f.AllowMultipleResponses == multiple));
        }

        if (requiresManualReview is { } review)
        {
            query = query.Where(f =>
                liveNodes.Any(n => n.FormId == f.Id && n.RequiresManualReview == review)
                || (!liveNodes.Any(n => n.FormId == f.Id) && f.RequiresManualReview == review));
        }

        return query;
    }

    public async Task<PagedResult<FormAllSummaryProjection>> GetAllFormsAsync(GetAllFormsRequest request, IReadOnlyList<Guid>? ownerOrder, CancellationToken ct = default)
    {
        var query = request.Status == FormStatus.Deleted
            ? _context.Forms.AsNoTracking().IgnoreQueryFilters().Where(f => f.Status == FormStatus.Deleted)
            : _context.Forms.AsNoTracking().Where(f => f.Status != FormStatus.Deleted);

        if (request.Status is { } status and not FormStatus.Deleted)
            query = query.Where(f => f.Status == status);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(f => EF.Functions.ILike(f.Title, $"%{term}%") || EF.Functions.ILike(f.Id.ToString(), $"{term}%"));
        }

        if (request.OwnerId is { } ownerId)
            query = query.Where(f => f.Collaborators.Any(c => c.UserId == ownerId && c.Role == CollaboratorRole.Owner));

        if (request.AllowAnonymous.HasValue)
            query = query.Where(f => f.AllowAnonymousResponses == request.AllowAnonymous.Value);

        query = ApplyWorkflowAwareFilters(query, request.AllowMultiple, request.RequiresManualReview);

        query = ApplyCatalogSorting(query, request.SortBy, string.Equals(request.SortDirection, "ascending", StringComparison.OrdinalIgnoreCase), ownerOrder);

        var totalCount = await query.CountAsync(ct);

        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                f.Responses.Count(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status)),
                f.Responses.Count(r => r.Status == FormResponseStatus.Pending && !r.IsArchived),
                f.Responses.Where(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status)).Max(r => (DateTime?)r.SubmittedAt)
            ))
            .ToListAsync(ct);

        return new PagedResult<FormAllSummaryProjection>(items, totalCount, page, pageSize);
    }

    private static IQueryable<Form> ApplyCatalogSorting(IQueryable<Form> query, string? sortBy, bool ascending, IReadOnlyList<Guid>? ownerOrder)
    {
        IOrderedQueryable<Form> By<TKey>(Expression<Func<Form, TKey>> key) => ascending ? query.OrderBy(key) : query.OrderByDescending(key);

        var owners = ownerOrder?.ToArray() ?? [];

        var ordered = sortBy?.Trim().ToLowerInvariant() switch
        {
            "status" => By(f => f.Status),
            "title" => By(f => f.Title),
            "owner" => By(f => Array.IndexOf(owners, f.Collaborators.Where(c => c.Role == CollaboratorRole.Owner).Select(c => c.UserId).FirstOrDefault())),
            "responsecount" => By(f => f.Responses.Count(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status))),
            "pendingcount" => By(f => f.Responses.Count(r => r.Status == FormResponseStatus.Pending && !r.IsArchived)),
            "lastresponseat" => By(f => f.Responses.Where(r => !r.IsArchived && !FormResponseRepository.Uncounted.Contains(r.Status)).Max(r => (DateTime?)r.SubmittedAt) ?? DateTime.MinValue),
            _ => By(f => f.UpdatedAt ?? f.CreatedAt),
        };

        return ordered.ThenBy(f => f.Id);
    }

    public async Task<IReadOnlyList<FormCatalogRow>> GetCatalogRowsAsync(CancellationToken ct = default) =>
        await _context.Forms.AsNoTracking().IgnoreQueryFilters()
            .Select(f => new FormCatalogRow(
                f.Status,
                f.Collaborators.Where(c => c.Role == CollaboratorRole.Owner).Select(c => c.UserId).FirstOrDefault(),
                f.Responses.Count(r => r.Status == FormResponseStatus.Pending && !r.IsArchived)))
            .ToListAsync(ct);

    public Task<Form?> GetDeletedForEditAsync(Guid id, CancellationToken ct = default) =>
        _context.Forms.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == id && f.Status == FormStatus.Deleted, ct);

    public void Add(Form form) => _context.Forms.Add(form);
}
