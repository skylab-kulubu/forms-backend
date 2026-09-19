using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class ComponentGroupRepository : IComponentGroupRepository
{
    private readonly FormsDbContext _context;

    public ComponentGroupRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<ComponentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _context.ComponentGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<ComponentGroup?> GetForEditAsync(Guid id, CancellationToken ct = default) =>
        _context.ComponentGroups.FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<ComponentGroup?> GetForLifecycleEditAsync(Guid id, CancellationToken ct = default) =>
        _context.ComponentGroups.IgnoreQueryFilters().FirstOrDefaultAsync(g => g.Id == id, ct);

    public async Task<PagedResult<ComponentGroupContract>> GetUserGroupsAsync(Guid userId, GetComponentGroupsRequest request, CancellationToken ct = default)
    {
        var query = request.Lifecycle switch
        {
            ComponentGroupLifecycleVisibility.Current => _context.ComponentGroups.AsNoTracking(),
            ComponentGroupLifecycleVisibility.Inactive => _context.ComponentGroups.IgnoreQueryFilters().AsNoTracking().Where(g => g.ArchivedAt != null),
            ComponentGroupLifecycleVisibility.All => _context.ComponentGroups.IgnoreQueryFilters().AsNoTracking(),
            _ => throw new ArgumentOutOfRangeException(nameof(request.Lifecycle))
        };

        query = query.Where(g => g.OwnedBy == userId);

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(g => EF.Functions.ILike(g.Title, $"%{request.Search.Trim()}%"));

        query = request.SortDirection?.ToLower() == "ascending"
            ? query.OrderBy(g => g.CreatedAt)
            : query.OrderByDescending(g => g.CreatedAt);

        var totalCount = await query.CountAsync(ct);

        var groups = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(g => new ComponentGroupContract(g.Id, g.Title, g.Description, g.Schema, null, g.ArchivedAt, g.ArchivedBy))
            .ToListAsync(ct);

        return new PagedResult<ComponentGroupContract>(groups, totalCount, request.Page, request.PageSize);
    }

    public void Add(ComponentGroup group) => _context.ComponentGroups.Add(group);
}
