using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Studio;

internal static class FormDocumentValues
{
    public static readonly string[] SettingKeys =
    [
        "title", "description", "status", "allowAnonymousResponses", "allowMultipleResponses", "requiresManualReview", "task", "closesAt", "timeLimitMinutes", "brand"
    ];

    private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal) { "__proto__", "constructor", "prototype" };

    public static bool IsSettingKey(string key) => SettingKeys.Contains(key, StringComparer.Ordinal);

    public static bool IsKey(string? key) => key is { Length: > 0 and <= StudioLimits.MaxKeyLength } && !ReservedKeys.Contains(key);

    public static JsonNode? DefaultOf(string key) => key switch
    {
        "title" => "",
        "status" => (int)FormStatus.Closed,
        "allowAnonymousResponses" or "allowMultipleResponses" or "requiresManualReview" => false,
        _ => null
    };

    public static bool TrySetting(string key, JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem) => key switch
    {
        "title" => TryTitle(value, out normalized, out problem),
        "description" => TryOptionalText(value, "Form açıklaması metin olmalı.", out normalized, out problem),
        "status" => TryStatus(value, out normalized, out problem),
        "allowAnonymousResponses" or "allowMultipleResponses" or "requiresManualReview" => TryFlag(value, out normalized, out problem),
        "task" => TryTask(value, out normalized, out problem),
        "closesAt" => TryDate(value, out normalized, out problem),
        "timeLimitMinutes" => TryMinutes(value, out normalized, out problem),
        "brand" => TryBrand(value, out normalized, out problem),
        _ => Fail("Bilinmeyen bir form ayarı gönderildi.", out normalized, out problem)
    };

    public static bool TrySchema(JsonNode? value, bool strict, [NotNullWhen(true)] out JsonArray? schema, [NotNullWhen(false)] out string? problem)
    {
        schema = new JsonArray();

        if (value is null)
        {
            problem = null;
            return true;
        }

        if (value is not JsonArray items)
        {
            schema = null;
            problem = "Form soruları bir liste olmalı.";
            return false;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (!strict && item is not JsonObject) continue;

            if (!TryField(item, strict, out var field, out problem))
            {
                schema = null;
                return false;
            }

            if (strict && !ids.Add((string)field["id"]!))
            {
                schema = null;
                problem = "Aynı kimlikle iki soru gönderildi.";
                return false;
            }

            schema.Add(field);
        }

        problem = null;
        return true;
    }

    public static bool TryField(JsonNode? value, bool strict, [NotNullWhen(true)] out JsonObject? field, [NotNullWhen(false)] out string? problem)
    {
        field = null;

        if (value is not JsonObject source)
            return Fail("Soru okunamadı.", out problem);

        if (!TryText(source["id"], out var id) || strict && !IsKey(id))
            return Fail("Sorunun kimliği okunamadı.", out problem);

        if (!TryText(source["type"], out var type) || strict && !IsKey(type))
            return Fail("Sorunun tipi okunamadı.", out problem);

        if (!TryProps(source["props"], strict, out var props, out problem))
            return false;

        if (!TryCondition(source["condition"], out var condition, out problem))
            return false;

        if (type == "link")
        {
            type = "short_text";
            props["inputType"] = "link";
            props["allowMultiple"] ??= false;
        }

        field = new JsonObject { ["id"] = id, ["type"] = type, ["props"] = props };
        if (condition is not null) field["condition"] = condition;

        return true;
    }

    public static bool TryType(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        if (TryText(value, out var type) && IsKey(type))
        {
            normalized = type;
            problem = null;
            return true;
        }

        return Fail("Sorunun tipi okunamadı.", out normalized, out problem);
    }

    public static bool TryProps(JsonNode? value, bool strict, [NotNullWhen(true)] out JsonObject? props, [NotNullWhen(false)] out string? problem)
    {
        props = new JsonObject();

        if (value is null)
        {
            problem = null;
            return true;
        }

        if (value is not JsonObject source)
        {
            props = null;
            return Fail("Soru ayarları okunamadı.", out problem);
        }

        foreach (var (key, item) in source)
        {
            if (!IsKey(key))
            {
                if (!strict) continue;

                props = null;
                return Fail("Soru ayarında geçersiz bir ad var.", out problem);
            }

            if (item is not null) props[key] = item.DeepClone();
        }

        problem = null;
        return true;
    }

    public static bool TryCondition(JsonNode? value, out JsonObject? condition, [NotNullWhen(false)] out string? problem)
    {
        condition = null;
        problem = null;

        if (value is null) return true;

        if (value is not JsonObject source)
            return Fail("Sorunun koşulu okunamadı.", out problem);

        if (source.Count > 0) condition = (JsonObject)source.DeepClone();

        return true;
    }

    public static DateTime? ReadDate(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime
            : null;

    public static string WriteDate(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    private static bool TryTitle(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        if (value is null)
        {
            normalized = "";
            problem = null;
            return true;
        }

        if (!TryText(value, out var title))
            return Fail("Form başlığı metin olmalı.", out normalized, out problem);

        if (title.Length > Form.TitleMaxLength)
            return Fail($"Form başlığı en fazla {Form.TitleMaxLength} karakter olabilir.", out normalized, out problem);

        normalized = title;
        problem = null;
        return true;
    }

    private static bool TryOptionalText(JsonNode? value, string message, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        normalized = null;
        problem = null;

        if (value is null) return true;
        if (!TryText(value, out var text)) return Fail(message, out problem);

        normalized = text;
        return true;
    }

    private static bool TryStatus(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        if (value is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue<int>(out var status)
            && status is (int)FormStatus.Open or (int)FormStatus.Closed)
        {
            normalized = status;
            problem = null;
            return true;
        }

        return Fail("Form yalnız açık ya da kapalı olabilir.", out normalized, out problem);
    }

    private static bool TryFlag(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        if (value is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            normalized = flag.GetValueKind() == JsonValueKind.True;
            problem = null;
            return true;
        }

        return Fail("Form ayarı açık ya da kapalı olmalı.", out normalized, out problem);
    }

    private static bool TryTask(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        normalized = null;
        problem = null;

        if (value is null) return true;

        if (value is not JsonObject task || !TryText(task["content"], out var content))
            return Fail("Görev okunamadı.", out problem);

        normalized = new JsonObject
        {
            ["content"] = content,
            ["collapsible"] = FlagOr(task["collapsible"], true),
            ["downloadable"] = FlagOr(task["downloadable"], true)
        };

        return true;
    }

    private static bool TryDate(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        normalized = null;
        problem = null;

        if (value is null) return true;

        if (!TryText(value, out var text) || ReadDate(text) is not { } date)
            return Fail("Kapanış zamanı okunamadı.", out problem);

        normalized = WriteDate(date);
        return true;
    }

    private static bool TryMinutes(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        normalized = null;
        problem = null;

        if (value is null) return true;

        if (value is not JsonValue number || number.GetValueKind() != JsonValueKind.Number || !number.TryGetValue<int>(out var minutes))
            return Fail("Kişisel süre tam sayı olmalı.", out problem);

        normalized = minutes;
        return true;
    }

    private static bool TryBrand(JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        normalized = null;
        problem = null;

        if (value is null) return true;

        if (!TryText(value, out var brand) || !Form.IsBrandKey(brand))
            return Fail("Tema okunamadı.", out problem);

        normalized = brand;
        return true;
    }

    private static bool FlagOr(JsonNode? value, bool fallback) =>
        value is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? flag.GetValueKind() == JsonValueKind.True
            : fallback;

    private static bool TryText(JsonNode? value, [NotNullWhen(true)] out string? text)
    {
        text = null;
        return value is JsonValue item && item.GetValueKind() == JsonValueKind.String && item.TryGetValue(out text);
    }

    private static bool Fail(string message, [NotNullWhen(false)] out string? problem)
    {
        problem = message;
        return false;
    }

    private static bool Fail(string message, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        normalized = null;
        problem = message;
        return false;
    }
}
