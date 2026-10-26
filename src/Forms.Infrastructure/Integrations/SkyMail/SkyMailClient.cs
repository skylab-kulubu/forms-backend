using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Infrastructure.Integrations.SkyMail;

internal class SkyMailClient : IEmailSender, IMailAudienceExport
{
    private static readonly JsonSerializerOptions JsonOptions = new(){ PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);

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

    // SkyMail'in tekli mail gövdesi; Forms'un EmailMessage'ı onun sözleşmesi değildir.
    private sealed record SingleMailTask(string TemplateKey, string RecipientEmail, string RecipientFullName, Dictionary<string, object> BodyVariables);

    public async Task<MailDelivery> SendAsync(EmailMessage request, CancellationToken ct = default)
    {
        try
        {
            // Outbox turu bir teslimi en çok bu kadar bekler; cevap vermeyen SkyMail diğer mesajları ve kilit süresini tüketmesin.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SendTimeout);

            var task = new SingleMailTask(request.TemplateKey, request.RecipientEmail, request.RecipientFullName, request.BodyVariables);
            using var response = await _httpClient.PostAsJsonAsync("mail_tasks/single", task, JsonOptions, timeout.Token);

            if (response.IsSuccessStatusCode) return MailDelivery.Sent;

            // Yalnız isteğin kendisini reddeden cevaplar kalıcıdır; 401, 403, 404 bir token ya da dağıtım anının geçici hâli olabilir.
            var refused = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnprocessableEntity;

            // Hata gövdesi alıcı adresini alıntılayabilir; yalnız durum yazılır.
            _logger.LogError("skymail /mail_tasks/single başarısız: {Status} (template {TemplateKey}){Outcome}", (int)response.StatusCode, request.TemplateKey, refused ? "; mail bırakıldı" : "; yeniden denenecek");

            return refused ? MailDelivery.Refused : MailDelivery.RetryLater;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("skymail /mail_tasks/single isteğinde hata ({Error}, template {TemplateKey}); yeniden denenecek", ex.GetType().Name, request.TemplateKey);
            return MailDelivery.RetryLater;
        }
    }
}
