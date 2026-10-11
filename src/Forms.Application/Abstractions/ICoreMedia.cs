namespace Skylab.Forms.Application.Abstractions;

public interface ICoreMedia
{
    Task<CoreMediaUpload> UploadGuestAnswerAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default);
    Task<CoreMediaRead> GetAsync(Guid mediaId, CancellationToken ct = default);
    Task<CoreMediaAttach> AttachAsync(Guid mediaId, CoreMediaOwner owner, Guid? onBehalfOf, CancellationToken ct = default);
    Task<bool> DetachAsync(Guid mediaId, Guid attachmentId, CancellationToken ct = default);
    Task<CoreMediaLink> CreateLinkAsync(Guid mediaId, Guid onBehalfOf, CancellationToken ct = default);
}

/// <summary>Core'da bir dosyayı tutan Forms kaydı: bir cevap ya da bir kişinin bir formdaki taslağı.</summary>
public sealed record CoreMediaOwner(string Type, string Id)
{
    public static CoreMediaOwner Response(Guid responseId) => new("response", responseId.ToString());

    public static CoreMediaOwner Draft(Guid formId, Guid userId) => new("draft", $"{formId}:{userId}");
}

public sealed record CoreMedia(Guid Id, string? Name, string? Type, long Size, string? Url, string? Purpose, string? Visibility, string? Status, string? ScanResult, Guid? UploadedBy);

public enum CoreMediaOutcome
{
    Ok,
    NotFound,
    TooLarge,
    TypeNotAllowed,
    NameInvalid,
    RateLimited,
    Unavailable,
    NotLinkable,
    Scanning,
    Rejected,
    SubjectInactive,
    Failed
}

public sealed record CoreMediaUpload(CoreMediaOutcome Outcome, CoreMedia? Media = null, int? RetryAfterSeconds = null);

public sealed record CoreMediaRead(CoreMediaOutcome Outcome, CoreMedia? Media = null);

public sealed record CoreMediaAttach(CoreMediaOutcome Outcome, Guid? AttachmentId = null);

public sealed record CoreMediaLink(CoreMediaOutcome Outcome, string? Url = null, DateTime? ExpiresAt = null, string? ScanResult = null, int? RetryAfterSeconds = null);

public static class CoreMediaStatus
{
    public const string Scanning = "scanning";
    public const string Pending = "pending";
    public const string Attached = "attached";
    public const string Detached = "detached";
    public const string Rejected = "rejected";
}

public static class CoreMediaPurpose
{
    public const string AnswerFile = "answer_file";
    public const string AnswerFileGuest = "answer_file_guest";
    public const string Legacy = "legacy";
}

public static class CoreMediaVisibility
{
    public const string Private = "private";
}
