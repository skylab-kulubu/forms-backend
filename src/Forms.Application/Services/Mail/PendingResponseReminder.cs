using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Mail;

namespace Skylab.Forms.Application.Services.Mail;

public interface IPendingResponseReminder
{
    Task RemindAsync(CancellationToken ct = default);
}

public sealed class PendingResponseReminder(IFormResponseRepository responses, IUserDirectory users, IMailDispatcher mail, FormMailOptions options)
    : IPendingResponseReminder
{
    public async Task RemindAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(options.PendingReminderTemplateKey)) return;

        var cutoff = DateTime.UtcNow.AddHours(-options.ReminderThresholdHours);
        var overdueForms = await responses.GetOverduePendingByFormAsync(cutoff, ct);
        if (overdueForms.Count == 0) return;

        var byReviewer = new Dictionary<Guid, List<OverduePendingFormProjection>>();
        foreach (var form in overdueForms)
        {
            foreach (var reviewerId in form.ReviewerIds)
            {
                if (!byReviewer.TryGetValue(reviewerId, out var forms))
                    byReviewer[reviewerId] = forms = [];
                forms.Add(form);
            }
        }

        var reviewers = (await users.GetUsersAsync(byReviewer.Keys, ct))
            .Where(u => u.Email is not null)
            .ToDictionary(u => u.Id);

        if (reviewers.Count == 0) return;

        var queued = true;
        foreach (var (reviewerId, forms) in byReviewer)
        {
            if (!reviewers.TryGetValue(reviewerId, out var reviewer)) continue;

            var variables = new Dictionary<string, object>(MailNames.Of(reviewer))
            {
                ["totalPending"] = forms.Sum(f => f.PendingCount),
                ["forms"] = forms.Select(f => (object)new Dictionary<string, object>
                {
                    ["formId"] = f.FormId.ToString(),
                    ["formTitle"] = f.FormTitle,
                    ["pendingCount"] = f.PendingCount
                }).ToList()
            };

            queued &= await mail.EnqueueAsync(new EmailMessage(options.PendingReminderTemplateKey, reviewer.Email!, MailNames.Full(reviewer), variables), ct);
        }

        if (queued) await responses.MarkOverduePendingRemindedAsync(cutoff, DateTime.UtcNow, ct);
    }
}
