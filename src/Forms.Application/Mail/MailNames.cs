using System.Globalization;
using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Mail;

public static class MailNames
{
    private static readonly CultureInfo Culture = new("tr-TR");

    public static Dictionary<string, object> Of(UserContract user) => new()
    {
        ["recipientName"] = Full(user),
        ["firstName"] = Proper(user.FirstName)
    };

    public static string Full(UserContract user) => Proper(user.FullName);

    private static string Proper(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : Culture.TextInfo.ToTitleCase(name.Trim().ToLower(Culture));
}
