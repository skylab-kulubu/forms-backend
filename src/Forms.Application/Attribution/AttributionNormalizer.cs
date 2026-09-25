using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Attribution;

/// <summary>
/// Kaynak etiketlerini tek sözlüğe indirir: "IG", "insta" ve kısa linkin /ig eki aynı
/// Instagram satırına düşsün. Core tıklamaları da buradan geçirilerek yanıtlarla eşleşir.
/// </summary>
public static class AttributionNormalizer
{
    private const int MaxLength = 100;

    private static readonly Dictionary<string, string> SourceAliases = new(StringComparer.Ordinal)
    {
        ["ig"] = "instagram",
        ["insta"] = "instagram",
        ["wa"] = "whatsapp",
        ["wp"] = "whatsapp",
        ["li"] = "linkedin",
        ["twitter"] = "x",
        ["mail"] = "email",
        ["e-mail"] = "email",
        ["e-posta"] = "email",
        ["eposta"] = "email",
        ["web"] = "website",
        ["site"] = "website",
    };

    private static readonly Dictionary<string, string> MediumBySource = new(StringComparer.Ordinal)
    {
        ["instagram"] = "social",
        ["linkedin"] = "social",
        ["x"] = "social",
        ["whatsapp"] = "messaging",
        ["email"] = "email",
        ["qr"] = "print",
        ["website"] = "referral",
    };

    public static ResponseAttribution? Normalize(ResponseAttributionRequest? request)
    {
        if (request is null) return null;

        var source = NormalizeSource(request.Source);
        var medium = Clean(request.Medium);
        var campaign = Clean(request.Campaign);
        var term = Clean(request.Term);
        var content = Clean(request.Content);

        if (source is null && medium is null && campaign is null && term is null && content is null) return null;

        if (medium is null && source is not null && MediumBySource.TryGetValue(source, out var derived)) medium = derived;

        return new ResponseAttribution
        {
            Source = source,
            Medium = medium,
            Campaign = campaign,
            Term = term,
            Content = content
        };
    }

    public static string? NormalizeSource(string? raw)
    {
        var source = Clean(raw);
        if (source is null) return null;
        return SourceAliases.TryGetValue(source, out var canonical) ? canonical : source;
    }

    private static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var chars = raw.Where(c => !char.IsControl(c)).ToArray();
        var value = new string(chars).Trim().ToLowerInvariant();
        if (value.Length == 0) return null;

        return value.Length > MaxLength ? value[..MaxLength].Trim() : value;
    }
}
