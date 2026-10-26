using System.Globalization;

namespace Skylab.Forms.Application.Common;

/// <summary>
/// Kulübün saati. Konteyner UTC ve kültürsüz çalışır; kişiye gösterilen tarih, gün ve saat İstanbul saatiyle ve
/// Türkçe yazılır, günlere ve saatlere bölen sayımlar da bu saate göre böler.
/// </summary>
public static class ClubTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("tr-TR");

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Saat dilimi verisi olmayan imajda Türkiye'nin yaz saati uygulamayan sabit +3'ü kullanılır.
            return TimeZoneInfo.CreateCustomTimeZone("Europe/Istanbul", TimeSpan.FromHours(3), "Türkiye", "Türkiye");
        }
    }
}
