using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public interface IFormWorkflowRunService
{
    Task<ServiceResult<WorkflowRunOverviewContract>> GetOverviewAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<WorkflowRunContract>>> GetRunsAsync(
        Guid workflowId,
        GetWorkflowRunsRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, WorkflowRunOverviewContract>> GetOverviewsAsync(
        IReadOnlyCollection<Guid> workflowIds,
        CancellationToken cancellationToken = default);
}
