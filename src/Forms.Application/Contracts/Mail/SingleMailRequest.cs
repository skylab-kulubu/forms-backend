namespace Skylab.Forms.Application.Contracts.Mail;

public record SingleMailRequest(
    string TemplateKey,
    string RecipientEmail,
    string RecipientFullName,
    Dictionary<string, object> BodyVariables
);
