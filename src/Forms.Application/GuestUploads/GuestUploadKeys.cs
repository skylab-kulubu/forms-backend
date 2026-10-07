using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Skylab.Forms.Application.GuestUploads;

public static class GuestUploadKeys
{
    private const string Prefix = "forms:guest-upload:";
    private const int SessionIdBytes = 32;
    private const int SessionIdLength = 43;
    private const int SubjectLength = 32;
    private const string UnknownSubject = "unknown";

    public static string Session(string sessionId) => $"{Prefix}session:{sessionId}";

    public static string SessionFile(string sessionId, Guid mediaId) => $"{Prefix}session:{sessionId}:file:{mediaId}";

    public static string SessionCount(string sessionId) => $"{Prefix}session:{sessionId}:count";

    public static string IpSessions(string subject, long bucket) => $"{Prefix}ip:{subject}:sessions:{bucket}";

    public static string IpFiles(string subject, long bucket) => $"{Prefix}ip:{subject}:files:{bucket}";

    public static string FormFiles(Guid formId, long bucket) => $"{Prefix}form:{formId}:files:{bucket}";

    public static string NewSessionId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SessionIdBytes)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public static bool IsSessionId([NotNullWhen(true)] string? value) =>
        value is { Length: SessionIdLength } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public static string ClientSubject(IPAddress? address)
    {
        if (address is null) return UnknownSubject;

        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        var text = address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"{NetworkOf64(address)}/64"
            : address.ToString();

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..SubjectLength];
    }

    private static IPAddress NetworkOf64(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes);
    }
}
