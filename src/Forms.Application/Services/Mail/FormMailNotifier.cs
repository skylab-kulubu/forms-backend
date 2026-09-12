using System.Globalization;
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

        var variables = new Dictionary<string, object>
        {
            ["recipientName"] = recipient.FullName ?? string.Empty,
            ["formTitle"] = form.Title,
            ["submittedAt"] = response.SubmittedAt.ToString("dd MMMM yyyy, HH:mm", Culture),
            ["answers"] = answers
        };

        _dispatcher.Enqueue(new SingleMailRequest(_options.FormCopyTemplateId, recipient.Email, recipient.FullName ?? string.Empty, variables));
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

        var variables = new Dictionary<string, object>
        {
            ["recipientName"] = recipient.FullName ?? string.Empty,
            ["formTitle"] = form.Title,
            ["status"] = status,
            ["reviewNote"] = response.ReviewNote ?? string.Empty
        };

        if (response.ReviewedAt.HasValue)
            variables["reviewedAt"] = response.ReviewedAt.Value.ToString("dd MMMM yyyy, HH:mm", Culture);

        // Değişken adı mail şablonuyla uyumlu kalsın diye korunuyor; kaynağı artık
        // formun eski bağlantısı değil, akışın seçtiği sonraki adım.
        if (nextFormId.HasValue)
            variables["linkedFormId"] = nextFormId.Value.ToString();

        _dispatcher.Enqueue(new SingleMailRequest(_options.StatusChangedTemplateId, recipient.Email, recipient.FullName ?? string.Empty, variables));
    }
}
