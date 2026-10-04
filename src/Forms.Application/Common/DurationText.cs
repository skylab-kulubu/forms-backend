namespace Skylab.Forms.Application.Common;

public static class DurationText
{
    public static string Of(int minutes)
    {
        if (minutes % 1440 == 0) return $"{minutes / 1440} gün";
        if (minutes % 60 == 0) return $"{minutes / 60} saat";
        return minutes > 60 ? $"{minutes / 60} saat {minutes % 60} dakika" : $"{minutes} dakika";
    }
}
