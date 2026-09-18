namespace Skylab.Forms.Application.Abstractions;

public interface ICoreGuestApply
{
    Task<bool> ApplyAsync(Guid eventId, EventGuestIdentity guest, CancellationToken ct = default);
}
