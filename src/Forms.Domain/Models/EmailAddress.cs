using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;

namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Forms'un mail gönderebildiği adres. Misafirin kimlik alanı, işaretli e-posta sorusu ve e-posta alıcı listesi aynı kuralı
/// kullanır: kayıtta tutulan misafir adresi listeye her zaman girer.
/// </summary>
public static class EmailAddress
{
    private const int MaxLength = 254;

    /// <summary>Boşluksuz, tek @, alan adında nokta, en çok 254 karakter ve MailAddress'in değiştirmeden okuduğu adres.</summary>
    public static bool IsValid([NotNullWhen(true)] string? email) =>
        // Uzunluk ayrıştırıcıdan önce gelir: aşırı uzun girdiyi ayrıştırmaya gerek yok.
        email is { Length: > 0 and <= MaxLength }
        && !email.Any(char.IsWhiteSpace)
        && email.Count(character => character == '@') == 1
        && MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.');
}
