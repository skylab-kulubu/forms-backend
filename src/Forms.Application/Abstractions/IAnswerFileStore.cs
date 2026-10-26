namespace Skylab.Forms.Application.Abstractions;

public interface IAnswerFileStore
{
    bool IsConfigured { get; }

    Task<FileUpload> UploadGuestFileAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default);

    Task<FileLookup> FindAsync(Guid fileId, CancellationToken ct = default);

    Task<FileHold> HoldAsync(Guid fileId, FileHolder holder, Guid? onBehalfOf, CancellationToken ct = default);

    Task<bool> ReleaseAsync(Guid fileId, Guid holdId, CancellationToken ct = default);

    Task<FileLink> CreateDownloadLinkAsync(Guid fileId, Guid viewerId, CancellationToken ct = default);
}

public abstract record FileHolder
{
    public sealed record Response(Guid ResponseId) : FileHolder;

    public sealed record Draft(Guid FormId, Guid UserId) : FileHolder;
}

public enum FileOrigin
{
    Unknown,

    SignedInAnswer,

    GuestAnswer,

    Legacy
}

public enum FileState
{
    Unknown,

    Scanning,

    Unattached,

    Attached,

    Detached,

    Rejected
}

public sealed record StoredFile(
    Guid Id,
    string? Name,
    string? ContentType,
    long Size,
    FileOrigin Origin,
    FileState State,
    bool IsPrivate,
    string? PublicUrl,
    string? ScanResult,
    Guid? UploadedBy);

public enum FileStoreOutcome
{
    Ok,
    NotFound,
    TooLarge,
    TypeNotAllowed,
    NameInvalid,
    RateLimited,
    Unavailable,

    NotHoldable,

    Scanning,
    Rejected,

    SubjectInactive,

    Failed
}

public sealed record FileUpload(FileStoreOutcome Outcome, StoredFile? File = null, int? RetryAfterSeconds = null);

public sealed record FileLookup(FileStoreOutcome Outcome, StoredFile? File = null);

public sealed record FileHold(FileStoreOutcome Outcome, Guid? HoldId = null);

public sealed record FileLink(FileStoreOutcome Outcome, string? Url = null, DateTime? ExpiresAt = null, string? ScanResult = null, int? RetryAfterSeconds = null);
