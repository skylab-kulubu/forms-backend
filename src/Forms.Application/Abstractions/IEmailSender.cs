using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Application.Abstractions;

public interface IEmailSender
{
    Task<MailDelivery> SendAsync(EmailMessage message, CancellationToken ct = default);
}

public enum MailDelivery
{
    Sent,

    RetryLater,

    Refused
}

public interface IMailAudienceExport
{
    Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract audience, CancellationToken ct = default);
}

public enum MailAudienceOutcome
{
    Exported,

    TooLarge,

    Unavailable
}

public sealed record MailAudienceExport(MailAudienceOutcome Outcome, FormMailSourceContract? Source = null);
