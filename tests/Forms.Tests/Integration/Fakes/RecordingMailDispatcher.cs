using System.Collections.Concurrent;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;

namespace Skylab.Forms.Tests.Integration.Fakes;

public sealed class RecordingMailDispatcher : IMailDispatcher
{
    private readonly ConcurrentQueue<EmailMessage> _queued = new();

    public IEnumerable<EmailMessage> To(string email) => _queued.Where(mail => mail.RecipientEmail == email);

    public bool Enqueue(EmailMessage request)
    {
        _queued.Enqueue(request);
        return true;
    }
}
