using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

namespace Skylab.Forms.Domain.Models;

public static class EmailAddress
{
    private const int MaxLength = 254;

    public static bool IsValid([NotNullWhen(true)] string? email) =>
        email is { Length: > 0 and <= MaxLength }
        && !email.Any(char.IsWhiteSpace)
        && email.Count(character => character == '@') == 1
        && MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.');
}
