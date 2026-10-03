namespace Skylab.Forms.Domain.Common;

public static class AttemptLimits
{
    public static readonly TimeSpan SubmitGrace = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan ReminderCooldown = TimeSpan.FromHours(12);

    public const int MaxTimeLimitMinutes = 30 * 24 * 60;
    public const int MaxExtensionMinutes = 7 * 24 * 60;
    public const int MaxTaskLength = 50_000;
    public const int MaxNoteLength = 500;
}
