namespace Skylab.Forms.Application.Abstractions;

public interface ICoreMedia
{
    Task<CoreMediaUpload> UploadGuestAnswerAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default);
    Task<CoreMediaRead> GetAsync(Guid mediaId, CancellationToken ct = default);
    Task<CoreMediaAttach> AttachToResponseAsync(Guid mediaId, Guid responseId, CancellationToken ct = default);
    Task<bool> DetachAsync(Guid mediaId, Guid attachmentId, CancellationToken ct = default);
}

public sealed record CoreMedia(Guid Id, string? Name, string? Type, long Size, string? Url, string? Purpose, string? Visibility, string? Status, string? ScanResult);

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
    Failed
}

public sealed record CoreMediaUpload(CoreMediaOutcome Outcome, CoreMedia? Media = null, int? RetryAfterSeconds = null);

public sealed record CoreMediaRead(CoreMediaOutcome Outcome, CoreMedia? Media = null);

public sealed record CoreMediaAttach(CoreMediaOutcome Outcome, Guid? AttachmentId = null);

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
    public const string AnswerFileGuest = "answer_file_guest";
}
