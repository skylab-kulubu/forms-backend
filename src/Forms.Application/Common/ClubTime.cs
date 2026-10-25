namespace Skylab.Forms.Application.Common;

/// <summary>Kulübün saati. Konteyner UTC çalışır; kişiye gösterilen tarih İstanbul saatiyle yazılır.</summary>
public static class ClubTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

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
