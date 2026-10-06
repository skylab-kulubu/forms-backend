using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.ComponentGroup;

namespace Skylab.Forms.Application.Services;

public interface IComponentGroupService
{
    Task<ServiceResult<ComponentGroupContract>> CreateGroupAsync(ComponentGroupUpsertRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentGroupContract>> UpdateGroupAsync(Guid id, ComponentGroupUpsertRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<ComponentGroupContract>>> GetUserGroupsAsync(Guid userId, GetComponentGroupsRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentGroupContract>> GetGroupByIdAsync(Guid id, Guid userId, string? token, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteGroupAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ShareTokenContract>> CreateOrRefreshShareTokenAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentGroupContract>> CloneGroupAsync(Guid id, Guid userId, string token, CancellationToken cancellationToken = default);
    Task<ServiceResult<ComponentGroupMetaContract>> GetGroupMetaAsync(Guid id, string token, CancellationToken cancellationToken = default);
}
