namespace Skylab.Forms.Application.GuestUploads;

public sealed class GuestUploadOptions
{
    public bool Enabled { get; init; }
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(360);
    public int SessionMaxFiles { get; init; } = 10;
    public int IpSessionsPerMinute { get; init; } = 30;
    public int IpFilesPerMinute { get; init; } = 60;
    public int FormFilesPerMinute { get; init; } = 200;
    public int IpSubmitsPerMinute { get; init; } = 300;
    public int FormSubmitsPerMinute { get; init; } = 1000;
    public int UnverifiedIpSubmitsPerMinute { get; init; } = 20;
    public int UnverifiedFormSubmitsPerMinute { get; init; } = 60;
    public int UnverifiedSubmitsPerMinute { get; init; } = 300;
}
