using System.Globalization;
using Microsoft.Extensions.Configuration;
using Skylab.Forms.Application.GuestUploads;

namespace Skylab.Forms.Infrastructure.GuestUploads;

public sealed class GuestUploadSettings
{
    public bool Requested { get; private init; }
    public GuestUploadOptions Options { get; private init; } = new();

    public static GuestUploadSettings FromConfiguration(IConfiguration configuration, bool turnstileEnabled)
    {
        var mode = (Read(configuration, "FORMS_GUEST_UPLOADS", "Mode") ?? "off").Trim();
        var requested = string.Equals(mode, "on", StringComparison.OrdinalIgnoreCase);

        if (!requested && !string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FORMS_GUEST_UPLOADS must be exactly 'off' or 'on'.");

        var sessionMinutes = ReadBoundedInt(configuration, "FORMS_GUEST_UPLOAD_SESSION_MINUTES", "SessionMinutes", 360, minimum: 10, maximum: 1_440);
        var sessionMaxFiles = ReadBoundedInt(configuration, "FORMS_GUEST_UPLOAD_SESSION_MAX_FILES", "SessionMaxFiles", 10, minimum: 1, maximum: 100);
        var ipSessionsPerMinute = ReadBoundedInt(configuration, "FORMS_GUEST_UPLOAD_IP_SESSIONS_PER_MINUTE", "IpSessionsPerMinute", 30, minimum: 1, maximum: 10_000);
        var ipFilesPerMinute = ReadBoundedInt(configuration, "FORMS_GUEST_UPLOAD_IP_FILES_PER_MINUTE", "IpFilesPerMinute", 60, minimum: 1, maximum: 10_000);
        var formFilesPerMinute = ReadBoundedInt(configuration, "FORMS_GUEST_UPLOAD_FORM_FILES_PER_MINUTE", "FormFilesPerMinute", 200, minimum: 1, maximum: 100_000);

        return new GuestUploadSettings
        {
            Requested = requested,
            Options = new GuestUploadOptions
            {
                Enabled = requested && turnstileEnabled,
                SessionLifetime = TimeSpan.FromMinutes(sessionMinutes),
                SessionMaxFiles = sessionMaxFiles,
                IpSessionsPerMinute = ipSessionsPerMinute,
                IpFilesPerMinute = ipFilesPerMinute,
                FormFilesPerMinute = formFilesPerMinute
            }
        };
    }

    private static int ReadBoundedInt(
        IConfiguration configuration,
        string environmentName,
        string optionName,
        int fallback,
        int minimum,
        int maximum)
    {
        var value = Read(configuration, environmentName, optionName) ?? fallback.ToString(CultureInfo.InvariantCulture);

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"{environmentName} must be an integer between {minimum} and {maximum}.");
        }

        return parsed;
    }

    private static string? Read(IConfiguration configuration, string environmentName, string optionName)
    {
        return Environment.GetEnvironmentVariable(environmentName)
            ?? configuration[$"GuestUploads:{optionName}"];
    }
}
