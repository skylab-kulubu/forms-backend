namespace Skylab.Forms.Application.GuestUploads;

/// <summary>
/// Misafir yüklemesinin ayar olmayan sınırları. Etkinlik akşamı yükseltilmesi gerekenler, doğrulanmış gönderim
/// sınırları, <see cref="GuestUploadOptions"/>'ta.
/// </summary>
public static class GuestRateLimits
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(6);
    public const int SessionMaxFiles = 10;
    public const int IpSessionsPerMinute = 30;
    public const int IpFilesPerMinute = 60;
    public const int FormFilesPerMinute = 200;

    // Turnstile'a ulaşılamazken doğrulanmadan kabul edilen cevaplar: kesinti sürerken bir betiği yalnız bunlar durdurur.
    public const int UnverifiedIpSubmitsPerMinute = 20;
    public const int UnverifiedFormSubmitsPerMinute = 60;
    public const int UnverifiedSubmitsPerMinute = 300;
}
