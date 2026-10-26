using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Mail;

namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Forms'un mail gönderebildiği adres. Misafirin kimlik alanı, işaretli e-posta sorusu ve e-posta alıcı listesi aynı kuralı
/// kullanır: kayıtta tutulan misafir adresi listeye her zaman girer.
/// </summary>
public static class EmailAddress
{
    private const int MaxLength = 254;

    /// <summary>
    /// Boşluksuz ve görünmez karaktersiz, tek @, alan adında nokta, en çok 254 karakter ve MailAddress'in değiştirmeden
    /// okuduğu adres.
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? email) =>
        // Uzunluk ayrıştırıcıdan önce gelir: aşırı uzun girdiyi ayrıştırmaya gerek yok.
        email is { Length: > 0 and <= MaxLength }
        && !email.Any(character => char.IsWhiteSpace(character) || IsInvisible(character))
        && email.Count(character => character == '@') == 1
        && MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.');

    /// <summary>
    /// Yapıştırılan adresin kalıntılarını atar: görünmez biçim karakterleri, "mailto:" öneki, sondaki virgül ya da noktalı
    /// virgül ve çevreleyen açılı ayraçlar. Geçerliliğe karar vermez; IsValid'den önce çağrılır.
    /// </summary>
    public static string? Clean(string? value)
    {
        if (value is null) return null;

        var email = new string([.. value.Where(character => char.GetUnicodeCategory(character) != UnicodeCategory.Format)]).Trim();

        if (email.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) email = email["mailto:".Length..];
        email = email.TrimEnd(',', ';').Trim();
        if (email.Length > 1 && email[0] == '<' && email[^1] == '>') email = email[1..^1].Trim();

        return email;
    }

    private static bool IsInvisible(char character) =>
        char.GetUnicodeCategory(character) is UnicodeCategory.Format or UnicodeCategory.Control;
}
