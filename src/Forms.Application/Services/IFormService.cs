using Forms.Application.Contracts;

namespace Forms.Application.Services;

public interface IFormService
{
    Task<ServiceResult<FormContract>> UpsertFormAsync(FormUpsertContract contract, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormDisplayContract>> GetDisplayFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<FormSummaryContract>>> GetUserFormsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> LinkFormsAsync(Guid parentId, Guid childId, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<bool>> UnlinkFormAsync(Guid parentId, Guid userId, CancellationToken ct = default);
}