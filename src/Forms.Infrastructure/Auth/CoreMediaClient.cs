using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class CoreMediaClient : ICoreMedia
{
    private const string FormsService = "forms";
    private const string ResponseOwnerType = "response";
    private const string AnswerRole = "answer";
    private const string NameInvalidCode = "media_name_invalid";
    private const string SubjectInactiveCode = "media_link_subject_inactive";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly ILogger<CoreMediaClient> _logger;

    public CoreMediaClient(HttpClient httpClient, ILogger<CoreMediaClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CoreMediaUpload> UploadGuestAnswerAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(CoreMediaPurpose.AnswerFileGuest), "purpose");

            var file = new StreamContent(content);
            if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType)) file.Headers.ContentType = mediaType;
            form.Add(file, "file", fileName);

            using var response = await _httpClient.PostAsync("/v1/media", form, ct);
            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                var media = await ReadMediaAsync(response, ct);
                if (media is not null) return new CoreMediaUpload(CoreMediaOutcome.Ok, media);

                _logger.LogError("Core misafir dosyası yanıtında medya kaydı yok");
                return new CoreMediaUpload(CoreMediaOutcome.Failed);
            }

            var problem = await ReadProblemAsync(response, ct);
            var outcome = response.StatusCode switch
            {
                HttpStatusCode.RequestEntityTooLarge => CoreMediaOutcome.TooLarge,
                HttpStatusCode.UnsupportedMediaType => CoreMediaOutcome.TypeNotAllowed,
                HttpStatusCode.BadRequest when problem?.Code == NameInvalidCode => CoreMediaOutcome.NameInvalid,
                HttpStatusCode.TooManyRequests => CoreMediaOutcome.RateLimited,
                HttpStatusCode.ServiceUnavailable => CoreMediaOutcome.Unavailable,
                _ => CoreMediaOutcome.Failed
            };

            if (outcome is CoreMediaOutcome.Failed)
                _logger.LogError("Core misafir dosyasını kabul etmedi: {Status} {Code}", (int)response.StatusCode, problem?.Code);
            else if (outcome is CoreMediaOutcome.RateLimited or CoreMediaOutcome.Unavailable)
                _logger.LogWarning("Core misafir dosyasını şimdilik almadı: {Status} {Code}", (int)response.StatusCode, problem?.Code);

            return new CoreMediaUpload(outcome, RetryAfterSeconds: problem?.RetryAfterSeconds ?? RetryAfterOf(response));
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core'a misafir dosyası yüklenemedi");
            return new CoreMediaUpload(failure);
        }
    }

    public async Task<CoreMediaRead> GetAsync(Guid mediaId, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            using var response = await _httpClient.GetAsync($"/v1/media/{mediaId}", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var media = await ReadMediaAsync(response, timeout.Token);
                if (media is not null) return new CoreMediaRead(CoreMediaOutcome.Ok, media);

                _logger.LogError("Core medya kaydı okunamadı (medya {MediaId})", mediaId);
                return new CoreMediaRead(CoreMediaOutcome.Failed);
            }

            if (response.StatusCode == HttpStatusCode.NotFound) return new CoreMediaRead(CoreMediaOutcome.NotFound);

            var problem = await ReadProblemAsync(response, timeout.Token);
            _logger.LogWarning("Core medya kaydını vermedi: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, mediaId);
            return new CoreMediaRead(CoreMediaOutcome.Failed);
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core medya kaydı istenemedi (medya {MediaId})", mediaId);
            return new CoreMediaRead(failure);
        }
    }

    public async Task<CoreMediaAttach> AttachToResponseAsync(Guid mediaId, Guid responseId, Guid? onBehalfOf, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            var body = new
            {
                owner = new { service = FormsService, type = ResponseOwnerType, id = responseId.ToString() },
                role = AnswerRole,
                onBehalfOf
            };

            using var response = await _httpClient.PostAsJsonAsync($"/v1/media/{mediaId}/attachments", body, Json, timeout.Token);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
            {
                var attachment = await response.Content.ReadFromJsonAsync<AttachmentReply>(Json, timeout.Token);
                if (attachment is not null && attachment.Id != Guid.Empty) return new CoreMediaAttach(CoreMediaOutcome.Ok, attachment.Id);

                _logger.LogError("Core bağlantı yanıtında kimlik yok (medya {MediaId})", mediaId);
                return new CoreMediaAttach(CoreMediaOutcome.Failed);
            }

            var problem = await ReadProblemAsync(response, timeout.Token);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogInformation("Core dosyayı cevaba bağlamadı: {Code} (medya {MediaId})", problem?.Code, mediaId);
                return new CoreMediaAttach(CoreMediaOutcome.NotLinkable);
            }

            _logger.LogError("Core dosyayı cevaba bağlamadı: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, mediaId);
            return new CoreMediaAttach(CoreMediaOutcome.Failed);
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core'a bağlama isteği gönderilemedi (medya {MediaId})", mediaId);
            return new CoreMediaAttach(failure);
        }
    }

    public async Task<bool> DetachAsync(Guid mediaId, Guid attachmentId, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            using var response = await _httpClient.DeleteAsync($"/v1/media/{mediaId}/attachments/{attachmentId}", timeout.Token);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return true;

            var problem = await ReadProblemAsync(response, timeout.Token);
            _logger.LogWarning("Core misafir dosyasının bağlantısını kaldırmadı: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, mediaId);
            return false;
        }
        catch (Exception ex) when (Classify(ex, ct) is not null)
        {
            _logger.LogWarning(ex, "Core'a bağlantı kaldırma isteği gönderilemedi (medya {MediaId})", mediaId);
            return false;
        }
    }

    public async Task<CoreMediaLink> CreateLinkAsync(Guid mediaId, Guid onBehalfOf, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"/v1/media/{mediaId}/links", new { onBehalfOf }, Json, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var link = await response.Content.ReadFromJsonAsync<LinkReply>(Json, timeout.Token);
                if (link is { Url.Length: > 0 }) return new CoreMediaLink(CoreMediaOutcome.Ok, link.Url, link.ExpiresAt);

                _logger.LogError("Core okuma bağlantısı yanıtında adres yok (medya {MediaId})", mediaId);
                return new CoreMediaLink(CoreMediaOutcome.Failed);
            }

            var problem = await ReadProblemAsync(response, timeout.Token);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    return new CoreMediaLink(CoreMediaOutcome.Scanning, RetryAfterSeconds: problem?.RetryAfterSeconds ?? RetryAfterOf(response));
                case HttpStatusCode.Gone:
                    return new CoreMediaLink(CoreMediaOutcome.Rejected, ScanResult: problem?.ScanResult);
                case HttpStatusCode.NotFound:
                    return new CoreMediaLink(CoreMediaOutcome.NotFound);
                case HttpStatusCode.UnprocessableEntity when problem?.Code == SubjectInactiveCode:
                    return new CoreMediaLink(CoreMediaOutcome.SubjectInactive);
                case HttpStatusCode.ServiceUnavailable:
                    _logger.LogWarning("Core okuma bağlantısı veremedi: {Code} (medya {MediaId})", problem?.Code, mediaId);
                    return new CoreMediaLink(CoreMediaOutcome.Unavailable, RetryAfterSeconds: problem?.RetryAfterSeconds ?? RetryAfterOf(response));
                default:
                    _logger.LogError("Core okuma bağlantısı vermedi: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, mediaId);
                    return new CoreMediaLink(CoreMediaOutcome.Failed);
            }
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core'dan okuma bağlantısı istenemedi (medya {MediaId})", mediaId);
            return new CoreMediaLink(failure);
        }
    }

    private static CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(CallTimeout);
        return source;
    }

    private static CoreMediaOutcome? Classify(Exception exception, CancellationToken ct) => exception switch
    {
        HttpRequestException => CoreMediaOutcome.Unavailable,
        OperationCanceledException when !ct.IsCancellationRequested => CoreMediaOutcome.Unavailable,
        JsonException or NotSupportedException or InvalidOperationException => CoreMediaOutcome.Failed,
        _ => null
    };

    private static async Task<CoreMedia?> ReadMediaAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var media = await response.Content.ReadFromJsonAsync<CoreMedia>(Json, ct);
        return media is not null && media.Id != Guid.Empty ? media : null;
    }

    private static async Task<CoreProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<CoreProblem>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static int? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return (int)Math.Ceiling(delta.TotalSeconds);
        if (retryAfter?.Date is { } date) return Math.Max(0, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds));
        return null;
    }

    private sealed record CoreProblem(string? Code, int? RetryAfterSeconds, string? ScanResult);

    private sealed record AttachmentReply(Guid Id);

    private sealed record LinkReply(string? Url, DateTime? ExpiresAt);
}
