using Forms.Application.Contracts;

namespace Forms.Application.Services;

public interface IFormService
{
    Task<FormContract> UpsertFormAsync(FormUpsertContract contract, Guid userId, CancellationToken cancellationToken = default);
    Task<FormContract?> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task <FormDisplayResult> GetDisplayFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<List<FormSummaryContract>> GetUserFormsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}