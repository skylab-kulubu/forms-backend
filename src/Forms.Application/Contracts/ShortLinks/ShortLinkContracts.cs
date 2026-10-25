using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Contracts.ShortLinks;

/// <param name="ManagedByEvent">ManagedExternally'nin eski adı; frontend taşınınca kaldırılacak.</param>
/// <param name="EventName">ManagerLabel'ın eski adı; frontend taşınınca kaldırılacak.</param>
/// <param name="ManagedExternally">Link başka bir sistemden yönetiliyor; kısa adı Forms'tan değiştirilemez.</param>
/// <param name="ManagerLabel">Linki yöneten sistemin ona verdiği ad.</param>
public record FormShortLinkContract(
    Guid Id,
    string Alias,
    int ClickCount,
    string? Label,
    bool ManagedByEvent,
    string? EventName,
    UserContract? CreatedBy,
    DateTime CreatedAt,
    bool ManagedExternally = false,
    string? ManagerLabel = null
);

public record ShortLinkRenameRequest(string? Alias);

public record AliasAvailabilityContract(string Alias, bool Available, string? Reason);

public record QrImageContract(byte[] Content, string ContentType, string FileName);
