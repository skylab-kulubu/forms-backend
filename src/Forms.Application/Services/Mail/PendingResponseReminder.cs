using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Mail;

namespace Skylab.Forms.Application.Services.Mail;

public interface IPendingResponseReminder
{
    /// <summary>Eşikten uzun süredir inceleme bekleyen cevapları inceleyenlerine bir kez hatırlatır.</summary>
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

        var queued = true;
        var reminded = new HashSet<Guid>();
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
            reminded.UnionWith(forms.Select(form => form.FormId));
        }

        // Hatırlatma bir kez gider; bir mail kuyruğa yazılamadıysa cevaplar damgalanmaz ki sonraki tur yeniden denesin.
        // Yazılabilmiş olanlar o turda bir kez daha gidebilir: kaybolan hatırlatmadan iyidir. İnceleyeni bulunamayan
        // formun (kişi dizini kapalı, kişi silinmiş) cevapları da damgalanmaz: o form hatırlatılmadı.
        if (queued && reminded.Count > 0) await responses.MarkOverduePendingRemindedAsync(cutoff, DateTime.UtcNow, reminded, ct);
    }
}
