using Forms.Application.Contracts;

namespace Forms.Application.Services;

public interface IFormResponseService
{
    Task<ServiceResult<Guid>> SubmitResponseAsync(FormResponseSubmitContract contract, Guid? userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ListResult<FormResponseSummaryContract>>> GetFormResponsesAsync(Guid formId, Guid userId, GetFormResponsesRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormResponseDetailContract>> GetResponseByIdAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> UpdateResponseStatusAsync(FormResponseStatusUpdateContract contract, Guid reviewerId, CancellationToken cancellationToken = default);
}