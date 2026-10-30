using System.Net;

namespace Skylab.Forms.Application.Abstractions;

public interface IHumanVerifier
{
    bool IsEnabled { get; }
    Task<HumanCheckVerdict> VerifyAsync(string? token, string action, IPAddress? remoteAddress, CancellationToken ct = default);
    Task<bool> IsReachableAsync(CancellationToken ct = default);
}

public enum HumanCheckVerdict
{
    Passed,
    Rejected,
    Unavailable,
    Disabled
}

public static class HumanCheckActions
{
    public const string GuestUpload = "guest-upload";
    public const string GuestSubmit = "guest-submit";
}
