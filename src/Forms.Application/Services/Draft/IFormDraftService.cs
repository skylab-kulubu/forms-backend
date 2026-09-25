using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Draft;
namespace Skylab.Forms.Application.Services;

public interface IFormDraftService
{
    Task<ServiceResult<bool>> SaveResponseDraftAsync(Guid formId, Guid userId, ResponseDraftRequest draft, CancellationToken ct = default);
    Task<ServiceResult<ResponseDraftContract?>> GetResponseDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteResponseDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> ClearResponseDraftsAsync(Guid formId, CancellationToken ct = default);

    Task<ServiceResult<bool>> SaveFormDraftAsync(Guid formId, Guid userId, FormDraftRequest draft, CancellationToken ct = default);
    Task<ServiceResult<FormDraftContract?>> GetFormDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteFormDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> ClearFormDraftsAsync(Guid formId, CancellationToken ct = default);
}
