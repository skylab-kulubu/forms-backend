using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Application.Services.Ownership;

public interface IOwnershipService
{
    Task<ServiceResult<bool>> TransferFormAsync(Guid formId, Guid targetUserId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> TransferTemplateAsync(Guid groupId, Guid targetUserId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> TransferWorkflowAsync(Guid workflowId, Guid targetUserId, Guid userId, CancellationToken ct = default);
}
