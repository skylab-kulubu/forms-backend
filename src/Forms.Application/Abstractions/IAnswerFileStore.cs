namespace Skylab.Forms.Application.Abstractions;

/// <summary>
/// Cevaplara yüklenen dosyaların deposu, Forms'un diliyle: dosyayı bulur, misafirinkini yükler, bir cevap ya da taslak
/// yaşadıkça tutar, sonra bırakır ve inceleyene okuma linki verir. Deponun kendi kavramları adapter'da kalır.
/// </summary>
public interface IAnswerFileStore
{
    Task<FileUpload> UploadGuestFileAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default);

    Task<FileLookup> FindAsync(Guid fileId, CancellationToken ct = default);

    /// <summary>Dosyayı tutan kayıt yaşadıkça saklar; depo, tutulmayan dosyayı bir süre sonra kendisi siler.</summary>
    Task<FileHold> HoldAsync(Guid fileId, FileHolder holder, Guid? onBehalfOf, CancellationToken ct = default);

    Task<bool> ReleaseAsync(Guid fileId, Guid holdId, CancellationToken ct = default);

    Task<FileLink> CreateDownloadLinkAsync(Guid fileId, Guid viewerId, CancellationToken ct = default);
}

/// <summary>Dosyayı tutan Forms kaydı: bir cevap ya da bir kişinin bir formdaki taslağı.</summary>
public abstract record FileHolder
{
    public sealed record Response(Guid ResponseId) : FileHolder;

    public sealed record Draft(Guid FormId, Guid UserId) : FileHolder;
}

/// <summary>Dosyanın Forms'a hangi yoldan geldiği.</summary>
public enum FileOrigin
{
    Unknown,

    /// <summary>Girişli kişinin kendi yüklediği cevap dosyası.</summary>
    SignedInAnswer,

    /// <summary>Misafirin Forms üzerinden yüklediği cevap dosyası.</summary>
    GuestAnswer,

    /// <summary>Bağlanmadan önceki eski yükleme; eskisi gibi kabul edilir, bağlanmaz.</summary>
    Legacy
}

public enum FileState
{
    Unknown,

    /// <summary>Güvenlik taraması sürüyor.</summary>
    Scanning,

    /// <summary>Taranmış, henüz bir kayda bağlanmamış.</summary>
    Unattached,

    Attached,

    /// <summary>Bağı kaldırılmış.</summary>
    Detached,

    /// <summary>Tarama dosyayı reddetti.</summary>
    Rejected
}

/// <param name="PublicUrl">Yalnız gizli olmayan eski dosyalarda dolu.</param>
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

    /// <summary>Depo dosyayı bu kayda bağlamadı (süresi dolmuş, başkasının ya da başka amaçla yüklenmiş).</summary>
    NotHoldable,

    Scanning,
    Rejected,

    /// <summary>Okuma linkini isteyen hesap artık etkin değil.</summary>
    SubjectInactive,

    Failed
}

public sealed record FileUpload(FileStoreOutcome Outcome, StoredFile? File = null, int? RetryAfterSeconds = null);

public sealed record FileLookup(FileStoreOutcome Outcome, StoredFile? File = null);

public sealed record FileHold(FileStoreOutcome Outcome, Guid? HoldId = null);

public sealed record FileLink(FileStoreOutcome Outcome, string? Url = null, DateTime? ExpiresAt = null, string? ScanResult = null, int? RetryAfterSeconds = null);
