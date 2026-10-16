using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Infrastructure.Mail;

public class SkyMailClient : ISkyMailService
{
    private static readonly JsonSerializerOptions JsonOptions = new(){ PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private const string SourceUnavailableMessage = "Kalıcı Forms mail bağlantısı şu an kullanılamıyor. Biraz sonra tekrar deneyin.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<SkyMailClient> _logger;

    public SkyMailClient(HttpClient httpClient, ILogger<SkyMailClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServiceResult<FormMailSourceContract>> PrepareFormMailSourceAsync(FormMailRecipientsContract request, CancellationToken ct = default)
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
                return new ServiceResult<FormMailSourceContract>(ServiceStatus.NotAcceptable, Message: "Bu formun alıcı listesi SkyMail sınırlarına uygun değil.");

            if (response.StatusCode != HttpStatusCode.Created)
            {
                _logger.LogError("skymail /form_mail_sources başarısız: {Status}", (int)response.StatusCode);
                return new ServiceResult<FormMailSourceContract>(ServiceStatus.ServiceUnavailable, Message: SourceUnavailableMessage);
            }

            var source = await response.Content.ReadFromJsonAsync<FormMailSourceResponse>(JsonOptions, ct);
            if (source is null || source.Id == Guid.Empty
                || source.FormId != request.FormId || source.OwnerSub != request.PreparedByUserId
                || source.FormTitle != request.FormTitle || source.RecipientCount != request.Recipients.Count
                || source.ResponseCount != request.ResponseCount || source.SkippedCount != request.SkippedCount
                || source.DuplicateCount != request.DuplicateCount || source.PreparedAt == default
                || source.ExpiresAt <= source.PreparedAt)
                return new ServiceResult<FormMailSourceContract>(ServiceStatus.ServiceUnavailable, Message: SourceUnavailableMessage);

            return new ServiceResult<FormMailSourceContract>(ServiceStatus.Success,
                Data: new FormMailSourceContract(source.Id, source.FormId, source.OwnerSub, source.FormTitle,
                    source.RecipientCount, source.ResponseCount, source.SkippedCount, source.DuplicateCount,
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
            return new ServiceResult<FormMailSourceContract>(ServiceStatus.ServiceUnavailable, Message: SourceUnavailableMessage);
        }
    }

    private sealed record FormMailSourceResponse(
        Guid Id, Guid FormId, Guid OwnerSub, string FormTitle, int RecipientCount,
        int ResponseCount, int SkippedCount, int DuplicateCount,
        DateTimeOffset PreparedAt, DateTimeOffset ExpiresAt);

    public async Task<bool> SendSingleAsync(SingleMailRequest request, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("mail_tasks/single", request, JsonOptions, ct);

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
