using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Infrastructure.Mail;

public class SkyMailClient : IEmailSender, IMailAudienceExport
{
    private static readonly JsonSerializerOptions JsonOptions = new(){ PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _httpClient;
    private readonly ILogger<SkyMailClient> _logger;

    public SkyMailClient(HttpClient httpClient, ILogger<SkyMailClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract request, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("form_mail_sources", new
            {
                OwnerSub = request.PreparedByUserId,
                request.FormId,
                request.FormTitle,
                Recipients = request.Recipients.Select(recipient => new { FullName = recipient.Name, recipient.Email }),
                request.ResponseCount,
                request.SkippedCount,
                request.DuplicateCount
            }, JsonOptions, ct);

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
                return new MailAudienceExport(MailAudienceOutcome.TooLarge);

            if (response.StatusCode != HttpStatusCode.Created)
            {
                _logger.LogError("skymail /form_mail_sources başarısız: {Status}", (int)response.StatusCode);
                return new MailAudienceExport(MailAudienceOutcome.Unavailable);
            }

            var source = await response.Content.ReadFromJsonAsync<FormMailSourceResponse>(JsonOptions, ct);
            if (source is null || source.Id == Guid.Empty
                || source.FormId != request.FormId || source.OwnerSub != request.PreparedByUserId
                || source.FormTitle != request.FormTitle || source.RecipientCount != request.Recipients.Count
                || source.ResponseCount != request.ResponseCount || source.SkippedCount != request.SkippedCount
                || source.DuplicateCount != request.DuplicateCount || source.PreparedAt == default
                || source.ExpiresAt <= source.PreparedAt)
                return new MailAudienceExport(MailAudienceOutcome.Unavailable);

            return new MailAudienceExport(MailAudienceOutcome.Exported, new FormMailSourceContract(source.Id, source.FormId, source.OwnerSub,
                source.FormTitle, source.RecipientCount, source.ResponseCount, source.SkippedCount, source.DuplicateCount,
                source.PreparedAt, source.ExpiresAt));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Taşıma/JSON hataları alıcı veya token içerebilir; hata içeriği yazılmaz.
            _logger.LogError("skymail /form_mail_sources isteği tamamlanamadı.");
            return new MailAudienceExport(MailAudienceOutcome.Unavailable);
        }
    }

    private sealed record FormMailSourceResponse(
        Guid Id, Guid FormId, Guid OwnerSub, string FormTitle, int RecipientCount,
        int ResponseCount, int SkippedCount, int DuplicateCount,
        DateTimeOffset PreparedAt, DateTimeOffset ExpiresAt);

    private sealed record SingleMailTask(string TemplateKey, string RecipientEmail, string RecipientFullName, Dictionary<string, object> BodyVariables);

    public async Task<bool> SendAsync(EmailMessage request, CancellationToken ct = default)
    {
        try
        {
            var task = new SingleMailTask(request.TemplateKey, request.RecipientEmail, request.RecipientFullName, request.BodyVariables);
            using var response = await _httpClient.PostAsJsonAsync("mail_tasks/single", task, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                // Hata gövdesi alıcı adresini alıntılayabilir; yalnız durum yazılır.
                _logger.LogError("skymail /mail_tasks/single başarısız: {Status} (template {TemplateKey})", (int)response.StatusCode, request.TemplateKey);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "skymail /mail_tasks/single isteğinde hata (template {TemplateKey})", request.TemplateKey);
            return false;
        }
    }
}
