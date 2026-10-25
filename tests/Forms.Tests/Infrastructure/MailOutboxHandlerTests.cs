using System.Text.Json;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Infrastructure.Mail;
using Skylab.Forms.Infrastructure.Outbox;

namespace Skylab.Forms.Tests.Infrastructure;

public class MailOutboxHandlerTests
{
    private sealed class ScriptedSender(MailDelivery delivery) : IEmailSender
    {
        public EmailMessage? Sent { get; private set; }

        public Task<MailDelivery> SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent = message;
            return Task.FromResult(delivery);
        }
    }

    [Theory]
    [InlineData(MailDelivery.Sent, true)]
    [InlineData(MailDelivery.Refused, true)]
    [InlineData(MailDelivery.RetryLater, false)]
    public async Task Only_a_mail_the_provider_could_not_take_now_stays_in_the_outbox(MailDelivery delivery, bool done)
    {
        var sender = new ScriptedSender(delivery);
        var message = new EmailMessage("forms.form-copy", "ayse@example.com", "Ayşe Yılmaz", new() { ["answers"] = new List<object> { new Dictionary<string, object?> { ["question"] = "Ad", ["answer"] = "Ayşe" } } });

        Assert.Equal(done, await new MailOutboxHandler(sender).HandleAsync(JsonSerializer.Serialize(message, OutboxMessage.Json), CancellationToken.None));
        Assert.Equal("Ayşe", ((JsonElement)sender.Sent!.BodyVariables["answers"])[0].GetProperty("answer").GetString());
    }
}
