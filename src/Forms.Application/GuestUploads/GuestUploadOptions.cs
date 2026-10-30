namespace Skylab.Forms.Application.GuestUploads;

public sealed class GuestUploadOptions
{
    public const string SectionName = "GuestUploads";

    public string Mode { get; set; } = "off";

    public bool Enabled => string.Equals(Mode.Trim(), "on", StringComparison.OrdinalIgnoreCase);

    public int IpSubmitsPerMinute { get; set; } = 300;

    public int FormSubmitsPerMinute { get; set; } = 1000;
}
