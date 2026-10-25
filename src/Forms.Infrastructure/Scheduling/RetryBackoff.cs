namespace Skylab.Forms.Infrastructure.Scheduling;

internal static class RetryBackoff
{
    private static readonly TimeSpan Max = TimeSpan.FromHours(1);

    public static TimeSpan After(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempts - 1), Max.TotalSeconds));
}
