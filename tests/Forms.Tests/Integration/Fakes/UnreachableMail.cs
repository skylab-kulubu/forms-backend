using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Tests.Integration.Fakes;

/// <summary>Mail aracına ulaşılamıyormuş gibi davranır; gönderilecek mailler testte RecordingMailDispatcher'da kalır.</summary>
public sealed class UnreachableMail : IEmailSender, IMailAudienceExport
{
    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default) => Task.FromResult(false);

    public Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract audience, CancellationToken ct = default) =>
        Task.FromResult(new MailAudienceExport(MailAudienceOutcome.Unavailable));
}
