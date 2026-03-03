using Skylab.Shared.Application.Contracts;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.ComponentGroup;

namespace Skylab.Forms.Application.Services;

public interface IComponentGroupService
{
    Task<ServiceResult<ComponentGroupContract>> CreateGroupAsync(ComponentGroupUpsertRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentGroupContract>> UpdateGroupAsync(Guid id, ComponentGroupUpsertRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<ComponentGroupContract>>> GetUserGroupsAsync(Guid userId, GetComponentGroupsRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentGroupContract>> GetGroupByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteGroupAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
