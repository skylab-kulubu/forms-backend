using Microsoft.Extensions.Logging;

namespace Skylab.Forms.Infrastructure.Integrations;

/// <summary>Bir sağlayıcının kayıt sırasında bıraktığı, açılışta bir kez loglanan not: kapalıysa neyin çalışmadığı.</summary>
public sealed record IntegrationNotice(LogLevel Level, string Message);

internal static class HttpUrl
{
    public static bool IsValid(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
}
