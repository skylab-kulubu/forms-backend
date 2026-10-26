using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

/// <summary>Cevap dosyalarını core'un /v1/media uçlarında tutar; core'un amaç, durum ve sahip kavramları burada kalır.</summary>
internal sealed class CoreAnswerFileStore : IAnswerFileStore
{
    private const string FormsService = "forms";
    private const string AnswerRole = "answer";
    private const string GuestAnswerPurpose = "answer_file_guest";
    private const string NameInvalidCode = "media_name_invalid";
    private const string SubjectInactiveCode = "media_link_subject_inactive";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly ILogger<CoreAnswerFileStore> _logger;
    private readonly TimeProvider _time;

    public CoreAnswerFileStore(HttpClient httpClient, TimeProvider time, ILogger<CoreAnswerFileStore> logger)
    {
        _httpClient = httpClient;
        _time = time;
        _logger = logger;
    }

    public bool IsConfigured => true;

    public async Task<FileUpload> UploadGuestFileAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(GuestAnswerPurpose), "purpose");

            var file = new StreamContent(content);
            if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType)) file.Headers.ContentType = mediaType;
            form.Add(file, "file", fileName);

            using var response = await _httpClient.PostAsync("/v1/media", form, ct);
            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)
            {
                var media = await ReadMediaAsync(response, ct);
                if (media is not null) return new FileUpload(FileStoreOutcome.Ok, media.ToStoredFile());

                _logger.LogError("Core misafir dosyası yanıtında medya kaydı yok");
                return new FileUpload(FileStoreOutcome.Failed);
            }

            var problem = await ReadProblemAsync(response, ct);
            var outcome = response.StatusCode switch
            {
                HttpStatusCode.RequestEntityTooLarge => FileStoreOutcome.TooLarge,
                HttpStatusCode.UnsupportedMediaType => FileStoreOutcome.TypeNotAllowed,
                HttpStatusCode.BadRequest when problem?.Code == NameInvalidCode => FileStoreOutcome.NameInvalid,
                HttpStatusCode.TooManyRequests => FileStoreOutcome.RateLimited,
                HttpStatusCode.ServiceUnavailable => FileStoreOutcome.Unavailable,
                _ => FileStoreOutcome.Failed
            };

            if (outcome is FileStoreOutcome.Failed)
                _logger.LogError("Core misafir dosyasını kabul etmedi: {Status} {Code}", (int)response.StatusCode, problem?.Code);
            else if (outcome is FileStoreOutcome.RateLimited or FileStoreOutcome.Unavailable)
                _logger.LogWarning("Core misafir dosyasını şimdilik almadı: {Status} {Code}", (int)response.StatusCode, problem?.Code);

            return new FileUpload(outcome, RetryAfterSeconds: problem?.RetryAfterSeconds ?? RetryAfterOf(response));
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core'a misafir dosyası yüklenemedi");
            return new FileUpload(failure);
        }
    }

    public async Task<FileLookup> FindAsync(Guid fileId, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            using var response = await _httpClient.GetAsync($"/v1/media/{fileId}", timeout.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var media = await ReadMediaAsync(response, timeout.Token);
                if (media is not null) return new FileLookup(FileStoreOutcome.Ok, media.ToStoredFile());

                _logger.LogError("Core medya kaydı okunamadı (medya {MediaId})", fileId);
                return new FileLookup(FileStoreOutcome.Failed);
            }

            if (response.StatusCode == HttpStatusCode.NotFound) return new FileLookup(FileStoreOutcome.NotFound);

            var problem = await ReadProblemAsync(response, timeout.Token);
            _logger.LogWarning("Core medya kaydını vermedi: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, fileId);
            return new FileLookup(FileStoreOutcome.Failed);
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core medya kaydı istenemedi (medya {MediaId})", fileId);
            return new FileLookup(failure);
        }
    }

    public async Task<FileHold> HoldAsync(Guid fileId, FileHolder holder, Guid? onBehalfOf, CancellationToken ct = default)
    {
        var (ownerType, ownerId) = OwnerOf(holder);

        using var timeout = LinkedTimeout(ct);
        try
        {
            var body = new
            {
                owner = new { service = FormsService, type = ownerType, id = ownerId },
                role = AnswerRole,
                onBehalfOf
            };

            using var response = await _httpClient.PostAsJsonAsync($"/v1/media/{fileId}/attachments", body, Json, timeout.Token);
            if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
            {
                var attachment = await response.Content.ReadFromJsonAsync<AttachmentReply>(Json, timeout.Token);
                if (attachment is not null && attachment.Id != Guid.Empty) return new FileHold(FileStoreOutcome.Ok, attachment.Id);

                _logger.LogError("Core bağlantı yanıtında kimlik yok (medya {MediaId})", fileId);
                return new FileHold(FileStoreOutcome.Failed);
            }

            var problem = await ReadProblemAsync(response, timeout.Token);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogInformation("Core dosyayı {OwnerType} kaydına bağlamadı: {Code} (medya {MediaId})", ownerType, problem?.Code, fileId);
                return new FileHold(FileStoreOutcome.NotHoldable);
            }

            _logger.LogError("Core dosyayı {OwnerType} kaydına bağlamadı: {Status} {Code} (medya {MediaId})", ownerType, (int)response.StatusCode, problem?.Code, fileId);
            return new FileHold(FileStoreOutcome.Failed);
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core'a bağlama isteği gönderilemedi (medya {MediaId})", fileId);
            return new FileHold(failure);
        }
    }

    public async Task<bool> ReleaseAsync(Guid fileId, Guid holdId, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            using var response = await _httpClient.DeleteAsync($"/v1/media/{fileId}/attachments/{holdId}", timeout.Token);
            if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.NotFound) return true;

            var problem = await ReadProblemAsync(response, timeout.Token);
            _logger.LogWarning("Core misafir dosyasının bağlantısını kaldırmadı: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, fileId);
            return false;
        }
        catch (Exception ex) when (Classify(ex, ct) is not null)
        {
            _logger.LogWarning(ex, "Core'a bağlantı kaldırma isteği gönderilemedi (medya {MediaId})", fileId);
            return false;
        }
    }

    public async Task<FileLink> CreateDownloadLinkAsync(Guid fileId, Guid viewerId, CancellationToken ct = default)
    {
        using var timeout = LinkedTimeout(ct);
        try
        {
            using var response = await _httpClient.PostAsJsonAsync($"/v1/media/{fileId}/links", new { onBehalfOf = viewerId }, Json, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                var link = await response.Content.ReadFromJsonAsync<LinkReply>(Json, timeout.Token);
                if (link is { Url.Length: > 0 }) return new FileLink(FileStoreOutcome.Ok, link.Url, link.ExpiresAt);

                _logger.LogError("Core okuma bağlantısı yanıtında adres yok (medya {MediaId})", fileId);
                return new FileLink(FileStoreOutcome.Failed);
            }

            var problem = await ReadProblemAsync(response, timeout.Token);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Conflict:
                    return new FileLink(FileStoreOutcome.Scanning, RetryAfterSeconds: problem?.RetryAfterSeconds ?? RetryAfterOf(response));
                case HttpStatusCode.Gone:
                    return new FileLink(FileStoreOutcome.Rejected, ScanResult: problem?.ScanResult);
                case HttpStatusCode.NotFound:
                    return new FileLink(FileStoreOutcome.NotFound);
                case HttpStatusCode.UnprocessableEntity when problem?.Code == SubjectInactiveCode:
                    return new FileLink(FileStoreOutcome.SubjectInactive);
                case HttpStatusCode.ServiceUnavailable:
                    _logger.LogWarning("Core okuma bağlantısı veremedi: {Code} (medya {MediaId})", problem?.Code, fileId);
                    return new FileLink(FileStoreOutcome.Unavailable, RetryAfterSeconds: problem?.RetryAfterSeconds ?? RetryAfterOf(response));
                default:
                    _logger.LogError("Core okuma bağlantısı vermedi: {Status} {Code} (medya {MediaId})", (int)response.StatusCode, problem?.Code, fileId);
                    return new FileLink(FileStoreOutcome.Failed);
            }
        }
        catch (Exception ex) when (Classify(ex, ct) is { } failure)
        {
            _logger.LogWarning(ex, "Core'dan okuma bağlantısı istenemedi (medya {MediaId})", fileId);
            return new FileLink(failure);
        }
    }

    // Core sahibi tür ve kimlikle tanır; taslağın kimliği form ve kişinin birleşimidir.
    private static (string Type, string Id) OwnerOf(FileHolder holder) => holder switch
    {
        FileHolder.Response response => ("response", response.ResponseId.ToString()),
        FileHolder.Draft draft => ("draft", $"{draft.FormId}:{draft.UserId}"),
        _ => throw new ArgumentOutOfRangeException(nameof(holder))
    };

    private static CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(ct);
        source.CancelAfter(CallTimeout);
        return source;
    }

    private static FileStoreOutcome? Classify(Exception exception, CancellationToken ct) => exception switch
    {
        HttpRequestException => FileStoreOutcome.Unavailable,
        OperationCanceledException when !ct.IsCancellationRequested => FileStoreOutcome.Unavailable,
        JsonException or NotSupportedException or InvalidOperationException => FileStoreOutcome.Failed,
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

    private int? RetryAfterOf(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return (int)Math.Ceiling(delta.TotalSeconds);
        if (retryAfter?.Date is { } date) return Math.Max(0, (int)Math.Ceiling((date - _time.GetUtcNow()).TotalSeconds));
        return null;
    }

    /// <summary>Core'un medya kaydı; bilinmeyen bir amaç ya da durum Unknown olur ve çağıranın varsayılan yoluna düşer.</summary>
    private sealed record CoreMedia(Guid Id, string? Name, string? Type, long Size, string? Url, string? Purpose, string? Visibility, string? Status, string? ScanResult, Guid? UploadedBy)
    {
        public StoredFile ToStoredFile() => new(
            Id,
            Name,
            Type,
            Size,
            Purpose switch
            {
                "answer_file" => FileOrigin.SignedInAnswer,
                GuestAnswerPurpose => FileOrigin.GuestAnswer,
                "legacy" => FileOrigin.Legacy,
                _ => FileOrigin.Unknown
            },
            Status switch
            {
                "scanning" => FileState.Scanning,
                "pending" => FileState.Unattached,
                "attached" => FileState.Attached,
                "detached" => FileState.Detached,
                "rejected" => FileState.Rejected,
                _ => FileState.Unknown
            },
            Visibility == "private",
            Url,
            ScanResult,
            UploadedBy);
    }

    private sealed record CoreProblem(string? Code, int? RetryAfterSeconds, string? ScanResult);

    private sealed record AttachmentReply(Guid Id);

    private sealed record LinkReply(string? Url, DateTime? ExpiresAt);
}
