using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IComponentGroupRepository
{
    Task<ComponentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ComponentGroup?> GetForEditAsync(Guid id, CancellationToken ct = default);
    Task<ComponentGroup?> GetForLifecycleEditAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<ComponentGroupContract>> GetUserGroupsAsync(Guid userId, GetComponentGroupsRequest request, CancellationToken ct = default);

    void Add(ComponentGroup group);
}
