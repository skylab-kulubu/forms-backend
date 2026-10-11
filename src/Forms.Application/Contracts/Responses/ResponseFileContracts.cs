namespace Skylab.Forms.Application.Contracts.Responses;

public record ResponseFileContract(
    Guid Id,
    string? Name,
    string? Type,
    long? Size,
    string Status,
    string? ScanResult,
    bool IsPrivate,
    string? Url
);

public record ResponseFileLinkContract(
    string? Url = null,
    DateTime? ExpiresAt = null,
    string? Reason = null,
    string? ScanResult = null,
    int? RetryAfterSeconds = null
);

public static class ResponseFileStatus
{
    public const string Scanning = "scanning";
    public const string Ready = "ready";
    public const string Rejected = "rejected";
    public const string Deleted = "deleted";
}

public static class ResponseFileReason
{
    public const string Scanning = "scanning";
    public const string Rejected = "rejected";
    public const string Deleted = "deleted";
    public const string SubjectInactive = "subjectInactive";
    public const string Unavailable = "unavailable";
}
