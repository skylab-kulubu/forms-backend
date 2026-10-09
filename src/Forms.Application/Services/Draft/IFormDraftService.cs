using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Domain.Models;
namespace Skylab.Forms.Application.Services;

public interface IFormDraftService
{
    Task<ServiceResult<bool>> SaveResponseDraftAsync(Guid formId, Guid userId, ResponseDraftRequest draft, CancellationToken ct = default);
    Task<ServiceResult<ResponseDraftContract?>> GetResponseDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteResponseDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> ClearResponseDraftsAsync(Guid formId, CancellationToken ct = default);
    Task RestoreResponseDraftAsync(Guid formId, Guid userId, List<FormResponseSchemaItem> responses, int timeSpent, List<FormResponseSchemaItem>? submission, CancellationToken ct = default);
    Task<bool> HoldsFileAsync(Guid formId, Guid userId, Guid mediaId, CancellationToken ct = default);

    Task<ServiceResult<bool>> SaveFormDraftAsync(Guid formId, Guid userId, FormDraftRequest draft, CancellationToken ct = default);
    Task<ServiceResult<FormDraftContract?>> GetFormDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> DeleteFormDraftAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> ClearFormDraftsAsync(Guid formId, CancellationToken ct = default);
}
