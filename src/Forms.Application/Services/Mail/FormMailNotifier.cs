using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;

namespace Skylab.Forms.Application.Services;

public class FormMailNotifier : IFormMailNotifier
{
    private static readonly CultureInfo Culture = new("tr-TR");
    private static readonly TimeSpan GuestCopyWindow = TimeSpan.FromDays(1);

    private readonly IUserDirectory _userService;
    private readonly IMailDispatcher _dispatcher;
    private readonly FormMailOptions _options;
    private readonly IFormResponseRepository _responses;
    private readonly ICacheService _cache;

    public FormMailNotifier(IUserDirectory userService, IMailDispatcher dispatcher, FormMailOptions options, IFormResponseRepository responses, ICacheService cache)
    {
        _userService = userService;
        _dispatcher = dispatcher;
        _options = options;
        _responses = responses;
        _cache = cache;
    }

    public async Task NotifyResponseCopyAsync(Form form, FormResponse response, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.FormCopyTemplateKey)) return;
        if (response.Guest is { } guest && !await CanSendGuestCopyAsync(form, response, guest, ct)) return;

        var recipient = await GetRespondentAsync(response, ct);
        if (string.IsNullOrWhiteSpace(recipient?.Email)) return;

        var answers = response.Data
            .Select(d => (object)new Dictionary<string, object?>
            {
                ["question"] = d.Question,
                ["answer"] = d.Answer
            })
            .ToList();

        var variables = new Dictionary<string, object>(MailNames.Of(recipient))
        {
            ["formTitle"] = form.Title,
            ["submittedAt"] = ClubTime.ToLocal(response.SubmittedAt).ToString("dd MMMM yyyy, HH:mm", Culture),
            ["answers"] = answers
        };

        _dispatcher.Enqueue(new SingleMailRequest(_options.FormCopyTemplateKey, recipient.Email, MailNames.Full(recipient), variables));
    }

    public async Task NotifyStatusChangedAsync(Form form, FormResponse response, Guid? nextFormId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.StatusChangedTemplateKey)) return;

        var status = response.Status switch
        {
            FormResponseStatus.Approved => "approved",
            FormResponseStatus.Declined => "declined",
            FormResponseStatus.Returned => "returned",
            _ => null
        };

        if (status is null) return;

        var recipient = await GetRespondentAsync(response, ct);
        if (string.IsNullOrWhiteSpace(recipient?.Email)) return;

        var variables = new Dictionary<string, object>(MailNames.Of(recipient))
        {
            ["formTitle"] = form.Title,
            ["status"] = status,
            ["reviewNote"] = response.ReviewNote ?? string.Empty
        };

        if (response.ReviewedAt.HasValue)
            variables["reviewedAt"] = ClubTime.ToLocal(response.ReviewedAt.Value).ToString("dd MMMM yyyy, HH:mm", Culture);

        if (nextFormId.HasValue)
            variables["nextFormId"] = nextFormId.Value.ToString();

        if (response.ReturnedQuestionIds is { Count: > 0 } flagged)
            variables["questionCount"] = flagged.Count;

        _dispatcher.Enqueue(new SingleMailRequest(_options.StatusChangedTemplateKey, recipient.Email, MailNames.Full(recipient), variables));
    }

    public async Task NotifyRunReopenedAsync(WorkflowRunVoided voided, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.StatusChangedTemplateKey)) return;

        var recipient = await _userService.GetUserAsync(voided.UserId, ct);
        if (string.IsNullOrWhiteSpace(recipient?.Email)) return;

        var variables = new Dictionary<string, object>(MailNames.Of(recipient))
        {
            ["formTitle"] = voided.WorkflowName,
            ["status"] = "reopened",
            ["reviewNote"] = voided.Note ?? string.Empty,
            ["reviewedAt"] = ClubTime.ToLocal(voided.VoidedAt).ToString("dd MMMM yyyy, HH:mm", Culture),
            ["nextFormId"] = voided.StartFormId.ToString()
        };

        _dispatcher.Enqueue(new SingleMailRequest(_options.StatusChangedTemplateKey, recipient.Email, MailNames.Full(recipient), variables));
    }

    public bool CanNotifyAttempts => !string.IsNullOrEmpty(_options.AttemptUpdateTemplateKey);

    public async Task NotifyAttemptAsync(Form form, Guid userId, string kind, DateTime? deadlineAt = null, int? minutes = null, Guid? nextFormId = null, CancellationToken ct = default)
    {
        if (!CanNotifyAttempts) return;

        var recipient = await _userService.GetUserAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(recipient?.Email)) return;

        var variables = new Dictionary<string, object>(MailNames.Of(recipient))
        {
            ["formTitle"] = form.Title,
            ["formId"] = form.Id.ToString(),
            ["kind"] = kind
        };

        if (deadlineAt.HasValue)
            variables["deadlineAt"] = ClubTime.ToLocal(deadlineAt.Value).ToString("dd MMMM yyyy, HH:mm", Culture);

        if (minutes.HasValue)
        {
            variables["minutes"] = minutes.Value;
            variables["duration"] = DurationText.Of(minutes.Value);
        }

        if (nextFormId.HasValue)
            variables["nextFormId"] = nextFormId.Value.ToString();

        _dispatcher.Enqueue(new SingleMailRequest(_options.AttemptUpdateTemplateKey, recipient.Email, MailNames.Full(recipient), variables));
    }

    /// <summary>Misafirin hesabı yok; mail formda yazdığı adrese gider.</summary>
    private async Task<UserContract?> GetRespondentAsync(FormResponse response, CancellationToken ct)
    {
        if (response.UserId is { } userId) return await _userService.GetUserAsync(userId, ct);

        return response.Guest is { } guest
            ? new UserContract(Guid.Empty, guest.Email, $"{guest.FirstName} {guest.LastName}", null, guest.FirstName)
            : null;
    }

    /// <summary>
    /// Misafirin adresi doğrulanmadığı için kopya başkasını spamlamaya yaramasın: aynı forma aynı
    /// adresle tek kopya, bir adrese de günde en fazla <see cref="FormMailOptions.GuestCopyDailyLimit"/> kopya gider.
    /// </summary>
    private async Task<bool> CanSendGuestCopyAsync(Form form, FormResponse response, ResponseGuest guest, CancellationToken ct)
    {
        if (await _responses.HasGuestResponseBeforeAsync(form.Id, guest.Email, response.SubmittedAt, ct)) return false;

        // Redis'e ulaşılamazsa günlük sınır atlanır; form başına tek kopya kuralı veritabanında olduğu için yine geçerli.
        var sent = await _cache.TryIncrementAsync(GuestCopyKey(guest.Email), GuestCopyWindow, ct);
        return sent is null || sent <= _options.GuestCopyDailyLimit;
    }

    /// <summary>
    /// Aynı kutuya giden yazımlar tek sayaçta toplanır: "+etiket" atılır, Gmail'de noktalar yok sayılır.
    /// Adres özetlenir ki Redis anahtarında açık durmasın.
    /// </summary>
    private static string GuestCopyKey(string email)
    {
        var address = email.ToLowerInvariant();
        var at = address.LastIndexOf('@');
        var local = address[..at];
        var domain = address[(at + 1)..];

        var tag = local.IndexOf('+');
        if (tag > 0) local = local[..tag];

        if (domain is "gmail.com" or "googlemail.com")
        {
            local = local.Replace(".", "");
            domain = "gmail.com";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{local}@{domain}")));
        return $"mail:guest-copies:{hash}";
    }
}
