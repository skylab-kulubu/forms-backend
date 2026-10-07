using System.Net;

namespace Skylab.Forms.Application.Abstractions;

public interface ITurnstileVerifier
{
    bool IsEnabled { get; }
    Task<TurnstileVerdict> VerifyAsync(string? token, string action, IPAddress? remoteAddress, CancellationToken ct = default);
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}

public enum TurnstileVerdict
{
    Passed,
    Rejected,
    Unavailable,
    Disabled
}

public static class TurnstileActions
{
    public const string GuestUpload = "guest-upload";
    public const string GuestSubmit = "guest-submit";
}
