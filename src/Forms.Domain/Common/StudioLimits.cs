namespace Skylab.Forms.Domain.Common;

public static class StudioLimits
{
    public const int MaxOpsPerBatch = 200;
    public const int MaxDocumentBytes = 512 * 1024;
    public const int MaxKeyLength = 64;
    public const int MaxCatchUpEdits = 500;
    public const int MaxEditAttempts = 5;
    public const int MaxCommentLength = 2000;

    public static readonly TimeSpan EditRetention = TimeSpan.FromDays(7);
}
