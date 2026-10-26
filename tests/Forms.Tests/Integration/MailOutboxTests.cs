using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Events;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Application.Services.Mail;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Mail;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class MailOutboxTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_queued_mail_waits_in_the_outbox_until_it_is_delivered()
    {
        var dispatcher = new OutboxMailDispatcher(api.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxMailDispatcher>.Instance);
        var recipient = $"{Guid.NewGuid():N}@forms.test";

        Assert.True(await dispatcher.EnqueueAsync(new EmailMessage("forms.form-copy", recipient, "Ayşe Yılmaz", new() { ["formTitle"] = "Başvuru" })));

        var mail = Assert.Single(await PendingMailsToAsync(recipient));
        Assert.Equal(("forms.form-copy", "Başvuru"), (mail.TemplateKey, mail.BodyVariables["formTitle"].ToString()));
    }

    [Fact]
    public async Task An_answer_waiting_past_the_threshold_reminds_its_reviewers_once()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var responseId = (await (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId!.Value;

        using (var scope = api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Responses
                .Where(response => response.Id == responseId)
                .ExecuteUpdateAsync(s => s.SetProperty(response => response.SubmittedAt, DateTime.UtcNow.AddDays(-3)));
        }

        await RemindAsync();
        await RemindAsync();

        var reminder = Assert.Single(api.Mail.To(owner.Email!), mail => mail.TemplateKey == "forms.pending-reminder");
        Assert.Equal(1, Convert.ToInt32(reminder.BodyVariables["totalPending"]));
    }

    [Fact]
    public async Task A_form_whose_reviewers_were_not_found_stays_due_for_its_reminder()
    {
        var known = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var unknown = api.ClientFor(Guid.NewGuid());
        var remindedForm = await known.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var unremindedForm = await unknown.CreateFormAsync(Api.NewForm("Gönüllü başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var applicant = api.ClientFor(api.Users.Add("Mehmet Kaya").Id);
        var reminded = (await (await applicant.SubmitAsync(remindedForm.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId!.Value;
        var unreminded = (await (await applicant.SubmitAsync(unremindedForm.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId!.Value;

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        await db.Responses.Where(r => r.Id == reminded || r.Id == unreminded)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.SubmittedAt, DateTime.UtcNow.AddDays(-3)));

        await RemindAsync();

        var sentAt = await db.Responses.AsNoTracking().Where(r => r.Id == reminded || r.Id == unreminded).ToDictionaryAsync(r => r.Id, r => r.PendingReminderSentAt);
        Assert.NotNull(sentAt[reminded]);
        Assert.Null(sentAt[unreminded]);
    }

    [Fact]
    public async Task Erasing_a_person_drops_the_mails_and_response_events_waiting_for_them()
    {
        var person = Guid.NewGuid();
        var address = $"{Guid.NewGuid():N}@forms.test";
        var someoneElse = $"{Guid.NewGuid():N}@forms.test";
        var now = DateTime.UtcNow;

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            db.Outbox.AddRange(
                OutboxMessage.Of(OutboxTypes.Mail, new EmailMessage("forms.status-changed", address.ToUpperInvariant(), "Ayşe", []), now),
                OutboxMessage.Of(OutboxTypes.Mail, new EmailMessage("forms.status-changed", someoneElse, "Can", []), now),
                OutboxMessage.Of(OutboxTypes.ResponseStatusChanged, new ResponseStatusChanged(Guid.NewGuid(), Guid.NewGuid(), FormResponseStatus.Approved, person, null), now));
            await db.SaveChangesAsync();

            await scope.ServiceProvider.GetRequiredService<IAccountErasureService>().EraseAsync(new AccountErasureCommand(Guid.NewGuid(), person, [address]));
        }

        Assert.Empty(await PendingMailsToAsync(address));
        Assert.Single(await PendingMailsToAsync(someoneElse));

        using var check = api.Services.CreateScope();
        var events = await check.ServiceProvider.GetRequiredService<FormsDbContext>().Outbox.AsNoTracking()
            .Where(message => message.Type == OutboxTypes.ResponseStatusChanged)
            .Select(message => message.Payload)
            .ToListAsync();
        Assert.DoesNotContain(events, payload => JsonSerializer.Deserialize<ResponseStatusChanged>(payload, OutboxMessage.Json)!.UserId == person);
    }

    private async Task RemindAsync()
    {
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPendingResponseReminder>().RemindAsync();
    }

    private async Task<List<EmailMessage>> PendingMailsToAsync(string recipient)
    {
        using var scope = api.Services.CreateScope();
        var payloads = await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Outbox.AsNoTracking()
            .Where(message => message.Type == OutboxTypes.Mail)
            .Select(message => message.Payload)
            .ToListAsync();

        return payloads
            .Select(payload => JsonSerializer.Deserialize<EmailMessage>(payload, OutboxMessage.Json)!)
            .Where(mail => string.Equals(mail.RecipientEmail, recipient, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
