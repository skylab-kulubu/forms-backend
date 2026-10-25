namespace Skylab.Forms.Application.Abstractions;

public interface IShortLinkProvider
{
    Task<ShortLinkResult> GetAsync(Guid formId, CancellationToken ct = default);

    Task<ShortLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default);

    /// <summary>Boş <paramref name="alias"/>, <paramref name="suggestion"/>'dan türeyen varsayılan ada döner. Eski ad aynı forma yönlenmeye devam eder.</summary>
    Task<ShortLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default);

    Task<AliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default);

    Task<ShortLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default);

    Task<QrImage?> GetQrAsync(string alias, bool svg, int size, QrTags tags, CancellationToken ct = default);
}

public sealed record QrImage(byte[] Content, string ContentType);

public sealed record QrTags(string Source, string? Medium, string? Campaign, string? Content);

public enum ShortLinkStatus
{
    Ok,
    NotFound,
    Conflict,
    Invalid,

    ManagedElsewhere,

    Failed
}

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
