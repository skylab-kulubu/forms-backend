using Forms.Application.Contracts;
using Forms.Application.Contracts.Responses;

namespace Forms.Application.Services;

public interface IFormResponseService
{
    Task<ServiceResult<Guid>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormResponsesListResult>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ArchiveResponseAsync(Guid responseId, Guid archiverId, CancellationToken cancellationToken = default);
}