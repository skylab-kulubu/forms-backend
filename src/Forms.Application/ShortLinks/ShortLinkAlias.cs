using System.Globalization;
using System.Text;

namespace Skylab.Forms.Application.ShortLinks;

/// <summary>
/// ADR 0033: varsayılan kısa ad okunabilir bir ad ve yıldır. Ad doluysa core sonuna -2, -3 ekler.
/// Önyüzdeki defaultAliasFromTitle aynı kuralı izler; ikisi birlikte değişmeli.
/// </summary>
public static class ShortLinkAlias
{
    private const int MaxTitleLength = 32;
    private const string Fallback = "form";

    public static string FromTitle(string? title, int year)
    {
        var slug = Slugify(title);
        if (slug.Length > MaxTitleLength)
        {
            var cut = slug.LastIndexOf('-', MaxTitleLength);
            slug = (cut > 0 ? slug[..cut] : slug[..MaxTitleLength]).TrimEnd('-');
        }
        if (slug.Length == 0) slug = Fallback;

        return slug.Split('-').Any(IsYear) ? slug : $"{slug}-{year}";
    }

    private static string Slugify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var builder = new StringBuilder(raw.Length);
        var pendingDash = false;
        foreach (var ch in raw.Replace('ı', 'i').Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark || ch is '\'' or '’') continue;

            var lower = char.ToLowerInvariant(ch);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0) builder.Append('-');
                builder.Append(lower);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return builder.ToString();
    }

    private static bool IsYear(string part) =>
        part.Length == 4 && (part.StartsWith("19", StringComparison.Ordinal) || part.StartsWith("20", StringComparison.Ordinal)) && part.All(char.IsAsciiDigit);
}
