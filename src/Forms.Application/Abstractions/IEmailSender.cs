using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Application.Abstractions;

public interface IEmailSender
{
    /// <summary>Maili gönderir; gönderemezse false döner ve çağıran yeniden dener.</summary>
    Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default);
}

/// <summary>Bir formun yanıtlayanlarını, sahibinin toplu mail yazabileceği mail aracına bir alıcı listesi olarak verir.</summary>
public interface IMailAudienceExport
{
    Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract audience, CancellationToken ct = default);
}

public enum MailAudienceOutcome
{
    Exported,

    /// <summary>Liste mail aracının sınırlarını aşıyor.</summary>
    TooLarge,

    Unavailable
}

public sealed record MailAudienceExport(MailAudienceOutcome Outcome, FormMailSourceContract? Source = null);
