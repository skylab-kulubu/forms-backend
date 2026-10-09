using System.Globalization;
using System.Text;
using System.Text.Json;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.GuestUploads;

public static class GuestUploadRules
{
    public const long MaxBytes = 50 * 1024 * 1024;
    public const string FileQuestionType = "file";
    public const string Pdf = "application/pdf";
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private const double BytesPerMegabyte = 1024 * 1024;
    private const string FallbackFileName = "dosya";

    public static readonly IReadOnlyList<string> Types = [Pdf, Jpeg, Png];

    /// <summary>Girişli kişinin yükleyebildiği türler: core'un answer_file amacının türleri.</summary>
    public static readonly IReadOnlyList<string> AccountTypes = [Pdf, Jpeg, Png, Docx];

    public static bool IsFileQuestion(FormSchemaItem item) =>
        string.Equals(item.Type, FileQuestionType, StringComparison.Ordinal);

    public static IReadOnlyList<string> EffectiveTypes(FormSchemaItem item) => EffectiveTypes(item, Types);

    public static IReadOnlyList<string> EffectiveAccountTypes(FormSchemaItem item) => EffectiveTypes(item, AccountTypes);

    private static IReadOnlyList<string> EffectiveTypes(FormSchemaItem item, IReadOnlyList<string> supported)
    {
        var rules = (ReadText(item, "acceptedFiles") ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (rules.Length == 0) return supported;

        var wanted = rules
            .SelectMany(rule => TypesOfRule(rule.ToLowerInvariant()))
            .ToHashSet(StringComparer.Ordinal);

        return [.. supported.Where(wanted.Contains)];
    }

    public static long EffectiveMaxBytes(FormSchemaItem item)
    {
        if (ReadNumber(item, "maxSize") is not { } megabytes || !(megabytes > 0)) return MaxBytes;

        var bytes = megabytes * BytesPerMegabyte;
        return bytes >= MaxBytes ? MaxBytes : (long)Math.Floor(bytes);
    }

    public static string? TypeOfFileName(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".pdf" => Pdf,
            ".jpg" or ".jpeg" or ".jpe" or ".jfif" => Jpeg,
            ".png" => Png,
            _ => null
        };

    public static string? TypeOfContentType(string? contentType) =>
        contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() switch
        {
            Pdf => Pdf,
            Jpeg or "image/jpg" or "image/pjpeg" => Jpeg,
            Png => Png,
            _ => null
        };

    public static string SanitizeFileName(string? fileName)
    {
        var name = fileName ?? string.Empty;
        var lastSegment = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        var cleaned = WithoutHiddenCharacters(lastSegment).Trim();

        return cleaned.Length > 0
            ? cleaned
            : FallbackFileName + WithoutHiddenCharacters(Path.GetExtension(name)).Trim();
    }

    private static string[] TypesOfRule(string rule) => rule switch
    {
        ".pdf" or "application/pdf" => [Pdf],
        ".docx" or Docx => [Docx],
        "application/*" => [Pdf, Docx],
        ".jpg" or ".jpeg" or ".jpe" or ".jfif" or "image/jpeg" or "image/jpg" or "image/pjpeg" => [Jpeg],
        ".png" or "image/png" => [Png],
        "image/*" => [Jpeg, Png],
        _ => []
    };

    private static string WithoutHiddenCharacters(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!IsHidden(character)) builder.Append(character);
        }

        return builder.ToString();
    }

    private static bool IsHidden(char character) =>
        char.IsControl(character)
        || character is >= '‪' and <= '‮'
        || character is >= '⁦' and <= '⁩'
        || character is '‎' or '‏' or '؜';

    private static string? ReadText(FormSchemaItem item, string key) =>
        Read(item, key) switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            string text => text,
            _ => null
        };

    private static double? ReadNumber(FormSchemaItem item, string key) =>
        Read(item, key) switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetDouble(out var number) => number,
            JsonElement { ValueKind: JsonValueKind.String } element => ParseNumber(element.GetString()),
            string text => ParseNumber(text),
            double number => number,
            float number => number,
            decimal number => (double)number,
            long number => number,
            int number => number,
            _ => null
        };

    private static object? Read(FormSchemaItem item, string key) =>
        item.Props is not null && item.Props.TryGetValue(key, out var value) ? value : null;

    private static double? ParseNumber(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
