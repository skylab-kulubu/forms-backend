namespace Skylab.Forms.Application.GuestUploads;

/// <summary>Misafir yüklemesinin kurulumdan kuruluma değişen ayarları; diğer sınırlar <see cref="GuestRateLimits"/>'te.</summary>
public sealed class GuestUploadOptions
{
    public const string SectionName = "GuestUploads";

    /// <summary>Tam olarak "on" ya da "off"; açık mod ancak bir insan doğrulaması da varsa çalışır.</summary>
    public string Mode { get; set; } = "off";

    public bool Enabled => string.Equals(Mode.Trim(), "on", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bir istemci adresinin dakikada gönderebileceği doğrulanmış misafir cevabı, bütün formlar birlikte.</summary>
    public int IpSubmitsPerMinute { get; set; } = 300;

    public int FormSubmitsPerMinute { get; set; } = 1000;
}
