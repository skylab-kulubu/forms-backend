namespace Skylab.Forms.Application.Common;

public static class UtcDate
{
    public static DateTime? Normalize(DateTime? value)
    {
        if (value is not { } date) return null;

        return date.Kind switch
        {
            DateTimeKind.Utc => date,
            DateTimeKind.Local => date.ToUniversalTime(),
            _ => DateTime.SpecifyKind(date, DateTimeKind.Utc)
        };
    }
}
