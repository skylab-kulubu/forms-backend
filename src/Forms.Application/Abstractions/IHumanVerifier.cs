using System.Net;

namespace Skylab.Forms.Application.Abstractions;

/// <summary>
/// Girişsiz bir isteğin bir insandan geldiğini doğrulayan bot koruması. Sağlayıcı yoksa IsEnabled false olur ve
/// misafir yüklemesi kapanır; karar kodları sağlayıcıdan bağımsızdır.
/// </summary>
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
