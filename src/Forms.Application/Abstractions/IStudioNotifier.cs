using Skylab.Forms.Application.Contracts.Studio;

namespace Skylab.Forms.Application.Abstractions;

public interface IStudioNotifier
{
    Task EditsAppliedAsync(Guid formId, StudioEditContract edit, CancellationToken ct = default);

    Task PublishedAsync(StudioPublishedContract published, CancellationToken ct = default);

    Task TeamChangedAsync(StudioTeamContract team, CancellationToken ct = default);
}
