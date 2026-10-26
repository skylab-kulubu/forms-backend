namespace Skylab.Forms.Domain.Enums;

public enum FormAvailability
{
    Open,

    /// <summary>Sahibi formu kapattı (ya da sildi).</summary>
    Closed,

    /// <summary>Süresiz formun kapanış saati geçti.</summary>
    ClosesAtPassed
}
