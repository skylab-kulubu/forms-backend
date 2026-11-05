using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormStudioRepository
{
    Task<FormWorkingCopy?> GetWorkingCopyAsync(Guid formId, CancellationToken ct = default);
    Task<FormWorkingCopy?> FindWorkingCopyAsync(Guid formId, CancellationToken ct = default);
    Task<FormEdit?> FindEditAsync(Guid formId, Guid batchId, CancellationToken ct = default);
    Task<List<FormEdit>> GetEditsAfterAsync(Guid formId, long version, int take, CancellationToken ct = default);

    void Add(FormWorkingCopy copy);
    void Add(FormEdit edit);
}
