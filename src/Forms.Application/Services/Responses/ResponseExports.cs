using System.Net.Mail;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Exports;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Responses;

/// <summary>Cevapların formdan dışarı çıktığı yerler: e-posta alıcı listesi, mail aracına kaynak ve Excel.</summary>
public interface IResponseExports
{
    Task<ServiceResult<FormMailRecipientsContract>> GetFormMailRecipientsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormMailSourceContract>> PrepareFormMailSourceAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<byte[]>> ExportResponsesToExcelAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
}

public sealed class ResponseExports : IResponseExports
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IUserDirectory _userService;
    private readonly IExcelService _excelService;
    private readonly IFormAttemptRepository _attemptRecords;
    private readonly IMailAudienceExport _mailAudience;
    private readonly ICurrentUser _currentUser;

    public ResponseExports(
        IFormRepository forms,
        IFormResponseRepository responses,
        IUserDirectory userService,
        IExcelService excelService,
        IFormAttemptRepository attemptRecords,
        IMailAudienceExport mailAudience,
        ICurrentUser currentUser)
    {
        _forms = forms;
        _responses = responses;
        _userService = userService;
        _excelService = excelService;
        _attemptRecords = attemptRecords;
        _mailAudience = mailAudience;
        _currentUser = currentUser;
    }

    public async Task<ServiceResult<FormMailRecipientsContract>> GetFormMailRecipientsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, cancellationToken);
        if (form == null)
            return new ServiceResult<FormMailRecipientsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<FormMailRecipientsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");

        var responses = await _responses.GetNonArchivedContactsAsync(formId, cancellationToken);
        var userIds = responses.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = (await _userService.GetUsersAsync(userIds, cancellationToken)).ToDictionary(u => u.Id);
        var recipients = new List<FormMailRecipientContract>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var duplicates = 0;

        foreach (var response in responses)
        {
            string? email;
            string name;
            if (response.UserId.HasValue)
            {
                users.TryGetValue(response.UserId.Value, out var user);
                email = user?.Email?.Trim();
                name = user?.FullName?.Trim() ?? "";
            }
            else
            {
                email = response.Guest?.Email?.Trim();
                name = $"{response.Guest?.FirstName?.Trim()} {response.Guest?.LastName?.Trim()}".Trim();
            }

            if (email is not { Length: > 0 and <= 254 }
                || email.Any(char.IsWhiteSpace) || email.Count(character => character == '@') != 1
                || !MailAddress.TryCreate(email, out var address)
                || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)
                || !address.Host.Contains('.'))
            {
                skipped++;
                continue;
            }

            if (!seen.Add(email))
            {
                duplicates++;
                continue;
            }

            recipients.Add(new FormMailRecipientContract(name, email));
        }

        return new ServiceResult<FormMailRecipientsContract>(ServiceStatus.Success,
            Data: new FormMailRecipientsContract(form.Id, userId, form.Title, recipients, responses.Count, skipped, duplicates));
    }

    public async Task<ServiceResult<FormMailSourceContract>> PrepareFormMailSourceAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await GetFormMailRecipientsAsync(formId, userId, cancellationToken);
        if (result.Status != ServiceStatus.Success)
            return new ServiceResult<FormMailSourceContract>(result.Status, Message: result.Message);

        var recipients = result.Data!;
        if (recipients.Recipients.Count == 0)
            return new ServiceResult<FormMailSourceContract>(ServiceStatus.NotAcceptable, Message: "E-posta gönderilebilecek yanıtlayan bulunamadı.");

        var export = await _mailAudience.ExportAsync(recipients, cancellationToken);

        return export switch
        {
            { Outcome: MailAudienceOutcome.Exported, Source: { } source } => new ServiceResult<FormMailSourceContract>(ServiceStatus.Success, source),
            { Outcome: MailAudienceOutcome.TooLarge } => new ServiceResult<FormMailSourceContract>(ServiceStatus.NotAcceptable, Message: "Bu formun alıcı listesi mail aracının sınırlarına uygun değil."),
            _ => new ServiceResult<FormMailSourceContract>(ServiceStatus.ServiceUnavailable, Message: "Kalıcı Forms mail bağlantısı şu an kullanılamıyor. Biraz sonra tekrar deneyin.")
        };
    }

    public async Task<ServiceResult<byte[]>> ExportResponsesToExcelAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, cancellationToken);

        if (form == null)
            return new ServiceResult<byte[]>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<byte[]>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını dışa aktarma yetkiniz yok.");

        var responses = await _responses.GetNonArchivedByFormAsync(formId, cancellationToken);

        var headers = new List<string>
        {
            "Yanıt ID",
            "Kullanıcı ID",
            "Gönderim Tarihi",
            "Durum",
            "İncelenme Notu",
            "Kaynak",
            "Ortam",
            "Kampanya"
        };

        var timings = form.HasTimeLimit
            ? await _attemptRecords.GetTimingsByResponseAsync(formId, cancellationToken)
            : new Dictionary<Guid, Abstractions.Storage.FormAttemptTiming>();

        if (form.HasTimeLimit)
            headers.AddRange(["Başlama Tarihi", "Teslim Süresi (dk)", "Ek Süre (dk)"]);

        foreach (var schemaItem in form.Schema)
        {
            string questionText = schemaItem.Props.TryGetValue("question", out var qVal) ? qVal?.ToString() ?? "İsimsiz Soru" : "Soru";
            headers.Add(questionText);
        }

        var rows = new List<List<string>>();

        foreach (var response in responses)
        {
            var row = new List<string>
            {
                response.Id.ToString(),
                response.UserId?.ToString() ?? "Anonim",
                ClubTime.ToLocal(response.SubmittedAt).ToString("dd.MM.yyyy HH:mm", ClubTime.Culture),
                StatusLabel(response.Status),
                response.ReviewNote ?? "",
                response.Attribution?.Source ?? "",
                response.Attribution?.Medium ?? "",
                response.Attribution?.Campaign ?? ""
            };

            if (form.HasTimeLimit)
            {
                var timing = timings.TryGetValue(response.Id, out var found) ? found : null;

                row.Add(timing?.StartedAt is { } started ? ClubTime.ToLocal(started).ToString("dd.MM.yyyy HH:mm", ClubTime.Culture) : "");
                row.Add(timing?.StartedAt is { } startedAt ? Math.Max(0, (int)Math.Round((response.SubmittedAt - startedAt).TotalMinutes)).ToString() : "");
                row.Add(timing is { ExtendedMinutes: > 0 } ? timing.ExtendedMinutes.ToString() : "");
            }

            foreach (var schemaItem in form.Schema)
            {
                var answerItem = response.Data.FirstOrDefault(d => d.Id == schemaItem.Id);
                row.Add(FormAnswerText.ToDisplayText(answerItem?.Answer));
            }

            rows.Add(row);
        }

        return new ServiceResult<byte[]>(ServiceStatus.Success, _excelService.GenerateExcel(new ExcelExportRequest(form.Title, headers, rows)));
    }

    // Arayüzdeki adlarla aynı: forms-frontend ResponseDisplayer.jsx STEP_STATUS ve ResponseActions.jsx STATUS_META.
    private static string StatusLabel(FormResponseStatus status) => status switch
    {
        FormResponseStatus.NonRestrict => "Gönderildi",
        FormResponseStatus.Pending => "Beklemede",
        FormResponseStatus.Approved => "Onaylandı",
        FormResponseStatus.Declined => "Reddedildi",
        FormResponseStatus.Provisional => "Geçici",
        FormResponseStatus.Flagged => "Doğrulanmadı",
        FormResponseStatus.Returned => "Düzeltme istendi",
        _ => status.ToString()
    };
}
