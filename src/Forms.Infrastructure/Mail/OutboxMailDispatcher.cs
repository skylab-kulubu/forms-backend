using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.Mail;

/// <summary>
/// Maili outbox'a yazar; bir yeniden başlatma ya da yavaş bir mail sağlayıcısı onu kaybettirmez. Kendi DbContext'ini
/// kullanır: çağıranın kaydedilmemiş değişikliklerini erkenden yazmaz.
/// </summary>
public sealed class OutboxMailDispatcher(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<OutboxMailDispatcher> logger) : IMailDispatcher
{
    public async Task<bool> EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

            db.Outbox.Add(OutboxMessage.Of(OutboxTypes.Mail, message, time.GetUtcNow().UtcDateTime));
            await db.SaveChangesAsync(ct);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Mail alıcının adresini ve adını taşır; log'a yalnız şablon ve hatanın türü yazılır.
            logger.LogError("Mail kuyruğa yazılamadı (template {TemplateKey}, {Error})", message.TemplateKey, ex.GetType().Name);
            return false;
        }
    }
}

/// <summary>Outbox'taki maili mail sağlayıcısına verir; sağlayıcının kalıcı olarak geri çevirdiği mail yeniden denenmez.</summary>
internal sealed class MailOutboxHandler(IEmailSender sender) : OutboxHandler<EmailMessage>
{
    public override string Type => OutboxTypes.Mail;

    // Bir günden geç gelen bildirim ya da hatırlatma artık kişiye yardım etmez.
    public override TimeSpan KeepTrying => TimeSpan.FromDays(1);

    protected override async Task<bool> HandleAsync(EmailMessage message, CancellationToken ct) =>
        await sender.SendAsync(message, ct) is not MailDelivery.RetryLater;
}
