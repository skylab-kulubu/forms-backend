namespace Skylab.Forms.Application.Abstractions;

/// <summary>
/// Core'daki forma bağlı kısa link. Link kişiye değil forma aittir; forms servis
/// hesabı yetkiyi kendisi denetleyip core'u çağırır.
/// </summary>
public interface ICoreShortLinks
{
    Task<CoreLinkResult> GetAsync(Guid formId, CancellationToken ct = default);
    /// <summary>Formun linkini döner, yoksa <paramref name="alias"/> adıyla açar; ad doluysa core onu numaralar.</summary>
    Task<CoreLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default);

    /// <summary>Boş <paramref name="alias"/>, <paramref name="suggestion"/>'dan türeyen varsayılan ada döner. Eski ad aynı forma yönlenmeye devam eder.</summary>
    Task<CoreLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default);
    Task<CoreAliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default);
    Task<CoreLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default);

    /// <summary>Kısa linkin QR'ı; ortada kulüp logosu, koda <paramref name="tags"/> gömülür.</summary>
    Task<CoreQrImage?> GetQrAsync(string alias, bool svg, int size, CoreQrTags tags, CancellationToken ct = default);
}

public sealed record CoreQrImage(byte[] Content, string ContentType);

/// <summary>QR'ın taşıyacağı utm etiketleri; boş olanlar koda yazılmaz.</summary>
public sealed record CoreQrTags(string Source, string? Medium, string? Campaign, string? Content);

public enum CoreLinkStatus
{
    Ok,
    NotFound,
    Conflict,
    Invalid,
    EventManaged,
    Failed
}

public sealed record CoreLink(
    Guid Id,
    string Alias,
    string Url,
    int ClickCount,
    Guid? CreatedBy,
    Guid? FormId,
    Guid? EventId,
    string? Label,
    DateTime CreatedAt
);

public sealed record CoreLinkResult(CoreLinkStatus Status, CoreLink? Link = null);

public sealed record CoreAliasAvailability(string Alias, bool Available, string? Reason);

/// <param name="Scans">Açılışların QR okutmasıyla gelen kısmı.</param>
public sealed record CoreSourceCount(string Source, int Count, int Scans = 0);

public sealed record CoreLinkStats(DateTime Since, int Total, List<CoreSourceCount> Sources);
