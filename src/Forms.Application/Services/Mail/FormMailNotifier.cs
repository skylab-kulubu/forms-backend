using System.Globalization;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Application.Services;

public class FormMailNotifier : IFormMailNotifier
{
    private static readonly CultureInfo Culture = new("tr-TR");

    private readonly IExternalUserService _userService;
    private readonly IMailDispatcher _dispatcher;
    private readonly FormMailOptions _options;

    public FormMailNotifier(IExternalUserService userService, IMailDispatcher dispatcher, FormMailOptions options)
    {
        _userService = userService;
        _dispatcher = dispatcher;
        _options = options;
    }

    public async Task NotifyResponseCopyAsync(Form form, FormResponse response, CancellationToken ct = default)
    {
        if (!response.UserId.HasValue || string.IsNullOrEmpty(_options.FormCopyTemplateId)) return;

        var recipient = await _userService.GetUserAsync(response.UserId.Value, ct);
        if (recipient?.Email is null) return;

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
            ["submittedAt"] = ToLocal(response.SubmittedAt).ToString("dd MMMM yyyy, HH:mm", Culture),
            ["answers"] = answers
        };

        _dispatcher.Enqueue(new SingleMailRequest(_options.FormCopyTemplateId, recipient.Email, MailNames.Full(recipient), variables));
    }

    public async Task NotifyStatusChangedAsync(Form form, FormResponse response, Guid? nextFormId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.StatusChangedTemplateId) || !response.UserId.HasValue) return;

        var status = response.Status switch
        {
            FormResponseStatus.Approved => "approved",
            FormResponseStatus.Declined => "declined",
            _ => null
        };

        if (status is null) return;

        var recipient = await _userService.GetUserAsync(response.UserId.Value, ct);
        if (recipient?.Email is null) return;

        var variables = new Dictionary<string, object>(MailNames.Of(recipient))
        {
            ["formTitle"] = form.Title,
            ["status"] = status,
            ["reviewNote"] = response.ReviewNote ?? string.Empty
        };

        if (response.ReviewedAt.HasValue)
            variables["reviewedAt"] = ToLocal(response.ReviewedAt.Value).ToString("dd MMMM yyyy, HH:mm", Culture);

        if (nextFormId.HasValue)
            variables["nextFormId"] = nextFormId.Value.ToString();

        _dispatcher.Enqueue(new SingleMailRequest(_options.StatusChangedTemplateId, recipient.Email, MailNames.Full(recipient), variables));
    }

    public bool CanNotifyAttempts => !string.IsNullOrEmpty(_options.AttemptUpdateTemplateId);

    public async Task NotifyAttemptAsync(Form form, Guid userId, string kind, DateTime? deadlineAt = null, int? minutes = null, Guid? nextFormId = null, CancellationToken ct = default)
    {
        if (!CanNotifyAttempts) return;

        var recipient = await _userService.GetUserAsync(userId, ct);
        if (recipient?.Email is null) return;

        var variables = new Dictionary<string, object>(MailNames.Of(recipient))
        {
            ["formTitle"] = form.Title,
            ["formId"] = form.Id.ToString(),
            ["kind"] = kind
        };

        if (deadlineAt.HasValue)
            variables["deadlineAt"] = ToLocal(deadlineAt.Value).ToString("dd MMMM yyyy, HH:mm", Culture);

        if (minutes.HasValue)
        {
            variables["minutes"] = minutes.Value;
            variables["duration"] = DurationText.Of(minutes.Value);
        }

        if (nextFormId.HasValue)
            variables["nextFormId"] = nextFormId.Value.ToString();

        _dispatcher.Enqueue(new SingleMailRequest(_options.AttemptUpdateTemplateId, recipient.Email, MailNames.Full(recipient), variables));
    }

    private static readonly TimeZoneInfo Istanbul = ResolveIstanbul();

    private static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Istanbul);

    private static TimeZoneInfo ResolveIstanbul()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Europe/Istanbul", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
        }
    }
}
