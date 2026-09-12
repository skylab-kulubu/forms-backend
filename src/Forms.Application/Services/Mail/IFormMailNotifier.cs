using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services;

public interface IFormMailNotifier
{
    Task NotifyResponseCopyAsync(Form form, FormResponse response, CancellationToken ct = default);
    /// <param name="nextFormId">Akış bu onaydan sonra bir forma yönlendiriyorsa o form.</param>
    Task NotifyStatusChangedAsync(Form form, FormResponse response, Guid? nextFormId = null, CancellationToken ct = default);
}
