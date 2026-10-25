namespace Skylab.Forms.Application.GuestUploads;

/// <summary>Misafir yüklemesinin kurulumdan kuruluma değişen ayarları; diğer sınırlar <see cref="GuestRateLimits"/>'te.</summary>
public sealed class GuestUploadOptions
{
    public bool Enabled { get; init; }

    /// <summary>Bir istemci adresinin dakikada gönderebileceği doğrulanmış misafir cevabı, bütün formlar birlikte.</summary>
    public int IpSubmitsPerMinute { get; init; } = 300;

    public int FormSubmitsPerMinute { get; init; } = 1000;
}
