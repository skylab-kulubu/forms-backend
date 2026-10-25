using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.Mail;

public sealed class OutboxMailDispatcher(IServiceScopeFactory scopeFactory, ILogger<OutboxMailDispatcher> logger) : IMailDispatcher
{
    public async Task<bool> EnqueueAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

            db.Outbox.Add(OutboxMessage.Of(OutboxTypes.Mail, message, DateTime.UtcNow));
            await db.SaveChangesAsync(ct);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Mail kuyruğa yazılamadı (template {TemplateKey}, {Error})", message.TemplateKey, ex.GetType().Name);
            return false;
        }
    }
}

internal sealed class MailOutboxHandler(IEmailSender sender) : OutboxHandler<EmailMessage>
{
    public override string Type => OutboxTypes.Mail;

    public override TimeSpan KeepTrying => TimeSpan.FromDays(1);

    protected override async Task<bool> HandleAsync(EmailMessage message, CancellationToken ct) =>
        await sender.SendAsync(message, ct) is not MailDelivery.RetryLater;
}
