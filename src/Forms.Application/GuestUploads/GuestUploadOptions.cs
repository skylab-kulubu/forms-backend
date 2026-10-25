namespace Skylab.Forms.Application.GuestUploads;

public sealed class GuestUploadOptions
{
    public bool Enabled { get; init; }

    public int IpSubmitsPerMinute { get; init; } = 300;

    public int FormSubmitsPerMinute { get; init; } = 1000;
}
