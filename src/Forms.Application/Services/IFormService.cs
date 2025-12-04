using Forms.Application.Contracts;

namespace Forms.Application.Services;

public interface IFormService
{
    Task<FormContract> UpsertAsync(FormUpsertContract contract, Guid userId, CancellationToken cancellationToken = default);
    Task<FormContract?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<FormSummaryContract>> GetUserFormsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}