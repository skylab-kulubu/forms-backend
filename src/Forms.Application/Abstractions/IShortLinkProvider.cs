namespace Skylab.Forms.Application.Abstractions;

/// <summary>
/// Forma bağlı kısa link. Link kişiye değil forma aittir; yetkiyi Forms kendisi denetler, sağlayıcı yalnız linki tutar.
/// </summary>
public interface IShortLinkProvider
{
    Task<ShortLinkResult> GetAsync(Guid formId, CancellationToken ct = default);

    /// <summary>Formun linkini döner, yoksa <paramref name="alias"/> adıyla açar; ad doluysa sağlayıcı onu numaralar.</summary>
    Task<ShortLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default);

    /// <summary>Boş <paramref name="alias"/>, <paramref name="suggestion"/>'dan türeyen varsayılan ada döner. Eski ad aynı forma yönlenmeye devam eder.</summary>
    Task<ShortLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default);

    Task<AliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default);

    Task<ShortLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default);

    /// <summary>Kısa linkin QR'ı; koda <paramref name="tags"/> gömülür.</summary>
    Task<QrImage?> GetQrAsync(string alias, bool svg, int size, QrTags tags, CancellationToken ct = default);
}

public sealed record QrImage(byte[] Content, string ContentType);

/// <summary>QR'ın taşıyacağı kaynak etiketleri; boş olanlar koda yazılmaz.</summary>
public sealed record QrTags(string Source, string? Medium, string? Campaign, string? Content);

public enum ShortLinkStatus
{
    Ok,
    NotFound,
    Conflict,
    Invalid,

    /// <summary>Link başka bir sistemden yönetiliyor; adı Forms'tan değiştirilemez.</summary>
    ManagedElsewhere,

    Failed
}

/// <param name="ManagedElsewhere">Link başka bir sistemden yönetiliyorsa true; o zaman Label o sistemin verdiği addır.</param>
public sealed record ShortLink(
    Guid Id,
    string Alias,
    string Url,
    int ClickCount,
    Guid? CreatedBy,
    string? Label,
    bool ManagedElsewhere,
    DateTime CreatedAt
);

public sealed record ShortLinkResult(ShortLinkStatus Status, ShortLink? Link = null);

public sealed record AliasAvailability(string Alias, bool Available, string? Reason);

/// <param name="Scans">Açılışların QR okutmasıyla gelen kısmı.</param>
public sealed record ShortLinkSourceCount(string Source, int Count, int Scans = 0);

public sealed record ShortLinkStats(DateTime Since, int Total, List<ShortLinkSourceCount> Sources);
