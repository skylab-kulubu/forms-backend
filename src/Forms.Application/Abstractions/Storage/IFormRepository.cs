using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormRepository
{
    Task<Form?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Form?> GetWithCollaboratorsAsync(Guid id, CancellationToken ct = default);
    Task<Form?> GetWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsUserCollaboratorAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid formId, CancellationToken ct = default);
    Task<bool> IsFormOpenAsync(Guid formId, CancellationToken ct = default);

    Task<Form?> GetForEditWithCollaboratorsAsync(Guid id, CancellationToken ct = default);
    Task<Form?> GetForEditWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Form?> GetForEditOwnedByAsync(Guid id, Guid ownerId, CancellationToken ct = default);

    Task<PagedResult<FormSummaryContract>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken ct = default);
    Task<PagedResult<FormAllSummaryProjection>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken ct = default);

    void Add(Form form);
}
