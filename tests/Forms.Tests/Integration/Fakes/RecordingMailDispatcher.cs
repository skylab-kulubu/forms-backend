using System.Collections.Concurrent;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;

namespace Skylab.Forms.Tests.Integration.Fakes;

public sealed class RecordingMailDispatcher : IMailDispatcher
{
    private readonly ConcurrentQueue<SingleMailRequest> _queued = new();

    public IEnumerable<SingleMailRequest> To(string email) => _queued.Where(mail => mail.RecipientEmail == email);

    public bool Enqueue(SingleMailRequest request)
    {
        _queued.Enqueue(request);
        return true;
    }
}
