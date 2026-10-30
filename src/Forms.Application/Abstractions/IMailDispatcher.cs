using Skylab.Forms.Application.Contracts.Mail;

namespace Skylab.Forms.Application.Abstractions;

public interface IMailDispatcher
{
    Task<bool> EnqueueAsync(EmailMessage message, CancellationToken ct = default);
}
