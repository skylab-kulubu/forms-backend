namespace Skylab.Forms.Application.Contracts.Mail;

/// <summary>Şablonlu bir mail: şablonu anahtarıyla, alıcıyı ve şablonun değişkenlerini taşır.</summary>
public record EmailMessage(
    string TemplateKey,
    string RecipientEmail,
    string RecipientFullName,
    Dictionary<string, object> BodyVariables
);
