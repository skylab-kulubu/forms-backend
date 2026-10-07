namespace Skylab.Forms.Application.GuestUploads;

public sealed class GuestUploadOptions
{
    public bool Enabled { get; init; }
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(360);
    public int SessionMaxFiles { get; init; } = 10;
    public int IpSessionsPerMinute { get; init; } = 30;
    public int IpFilesPerMinute { get; init; } = 60;
    public int FormFilesPerMinute { get; init; } = 200;
}
