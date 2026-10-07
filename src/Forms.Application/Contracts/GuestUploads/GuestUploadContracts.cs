namespace Skylab.Forms.Application.Contracts.GuestUploads;

public record GuestUploadsContract(long MaxBytes, List<string> Types);

public record TurnstileStatusContract(bool Enabled, bool Reachable);

public record GuestUploadSessionRequest(string? TurnstileToken);

public record GuestUploadSessionContract(
    string? SessionId = null,
    DateTime? ExpiresAt = null,
    string? Reason = null,
    int? RetryAfterSeconds = null
);

public record GuestUploadContract(
    Guid? Id = null,
    string? Name = null,
    string? Type = null,
    long? Size = null,
    string? Status = null,
    string? ScanResult = null,
    string? Reason = null,
    int? RetryAfterSeconds = null,
    long? MaxBytes = null
);

public static class GuestUploadReason
{
    public const string VerificationFailed = "verificationFailed";
    public const string GuestUploadsDisabled = "guestUploadsDisabled";
    public const string GuestUploadsUnavailable = "guestUploadsUnavailable";
    public const string SessionExpired = "sessionExpired";
    public const string TooManySessions = "tooManySessions";
    public const string TooManyUploads = "tooManyUploads";
    public const string SessionFileLimit = "sessionFileLimit";
    public const string QuestionNotFound = "questionNotFound";
    public const string FileEmpty = "fileEmpty";
    public const string FileTooLarge = "fileTooLarge";
    public const string FileTypeNotAllowed = "fileTypeNotAllowed";
    public const string FileNameInvalid = "fileNameInvalid";
    public const string FileScanning = "fileScanning";
    public const string FileRejected = "fileRejected";
    public const string FileExpired = "fileExpired";
    public const string TooManySubmissions = "tooManySubmissions";
    public const string SubmitUnavailable = "submitUnavailable";
}

public static class GuestUploadStatus
{
    public const string Scanning = "scanning";
    public const string Ready = "ready";
    public const string Rejected = "rejected";
    public const string Missing = "missing";
}
