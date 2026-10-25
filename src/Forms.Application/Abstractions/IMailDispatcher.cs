using Skylab.Forms.Application.Contracts.Mail;

namespace Skylab.Forms.Application.Abstractions;

public interface IMailDispatcher
{
    bool Enqueue(EmailMessage request);
}
