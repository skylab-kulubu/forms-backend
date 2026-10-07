using System.Net;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Application.Services;

public interface IFormResponseService
{
    Task<ServiceResult<ResponseSubmitResult>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, IPAddress? clientAddress, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormResponsesListResult>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, string? token, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ArchiveResponseAsync(Guid responseId, Guid archiverId, CancellationToken cancellationToken = default);
    Task<ServiceResult<byte[]>> ExportResponsesToExcelAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ShareTokenContract>> CreateOrRefreshShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> RevokeShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResponseMetaContract>> GetResponseMetaAsync(Guid responseId, string token, CancellationToken cancellationToken = default);
}
