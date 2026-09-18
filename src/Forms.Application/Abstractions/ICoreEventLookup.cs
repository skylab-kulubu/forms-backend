using Skylab.Forms.Application.Contracts.Forms;

namespace Skylab.Forms.Application.Abstractions;

public interface ICoreEventLookup
{
    Task<EventRefContract?> FindByIdAsync(Guid eventId, CancellationToken ct = default);
    Task<EventRefContract?> FindByFormIdAsync(Guid formId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, EventRefContract>> FindByFormIdsAsync(IEnumerable<Guid> formIds, CancellationToken ct = default);
}
