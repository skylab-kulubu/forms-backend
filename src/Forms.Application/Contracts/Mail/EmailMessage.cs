namespace Skylab.Forms.Application.Contracts.Mail;

public record EmailMessage(
    string TemplateKey,
    string RecipientEmail,
    string RecipientFullName,
    Dictionary<string, object> BodyVariables
);
