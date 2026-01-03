using Forms.Application.Contracts;
using Forms.Application.Contracts.Forms;

namespace Forms.Application.Services;

public interface IFormService
{
    Task<ServiceResult<FormContract>> UpsertFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<LinkableFormsContract>>> GetLinkableFormsAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}