using System.Globalization;
using System.Text.Json;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application;

public sealed record EventGuestIdentity(string FirstName, string LastName, string Email);

public static class EventIdentity
{
    public const string FirstName = "firstName";
    public const string LastName = "lastName";
    public const string Email = "email";

    public static readonly string[] Keys = [FirstName, LastName, Email];

    public static List<FormSchemaItem> Ensure(IReadOnlyList<FormSchemaItem>? schema)
    {
        var source = schema?.Select(Clone).ToList() ?? [];
        var used = new HashSet<string>(StringComparer.Ordinal);
        var byKey = new Dictionary<string, FormSchemaItem>(StringComparer.Ordinal);

        foreach (var field in source)
        {
            var key = ResolveKey(field, used);
            if (key is null) continue;
            Stamp(field, key);
            used.Add(key);
            byKey[key] = field;
        }

        var identity = Keys.Select(key =>
        {
            if (byKey.TryGetValue(key, out var existing)) return existing;
            return NewField(key);
        }).ToList();

        var rest = source.Where(field => Prop.Get(field, "identity") is null).ToList();
        identity.AddRange(rest);
        return identity;
    }

    public static EventGuestIdentity? Extract(
        IReadOnlyList<FormSchemaItem>? schema,
        IReadOnlyList<FormResponseSchemaItem>? answers)
    {
        var fields = Ensure(schema);
        string? first = null, last = null, email = null;
        foreach (var field in fields)
        {
            var key = Prop.Get(field, "identity");
            var answer = answers?.FirstOrDefault(row => row.Id == field.Id)?.Answer?.Trim();
            if (string.IsNullOrEmpty(answer)) continue;
            switch (key)
            {
                case FirstName: first = answer; break;
                case LastName: last = answer; break;
                case Email: email = answer; break;
            }
        }

        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(last) || string.IsNullOrEmpty(email) || !email.Contains('@'))
            return null;
        return new EventGuestIdentity(first, last, email);
    }

    private static FormSchemaItem NewField(string key)
    {
        var field = new FormSchemaItem
        {
            Id = $"identity:{key}",
            Type = "short_text",
            Props = new Dictionary<string, object?>()
        };
        Stamp(field, key);
        field.Props["question"] = key switch
        {
            FirstName => "Ad",
            LastName => "Soyad",
            _ => "E-posta"
        };
        return field;
    }

    private static void Stamp(FormSchemaItem field, string key)
    {
        field.Props["identity"] = key;
        field.Props["required"] = true;
        field.Props["inputType"] = key == Email ? "email" : "name";
        if (field.Condition is { Count: > 0 })
            field.Condition = new Dictionary<string, object?>();
    }

    private static string? ResolveKey(FormSchemaItem field, HashSet<string> used)
    {
        var marked = Prop.Get(field, "identity");
        if (marked is FirstName or LastName or Email && used.Add(marked))
            return marked;

        if (field.Type is not "short_text") return null;

        var input = Fold(Prop.Get(field, "inputType"));
        var question = Fold(Prop.Get(field, "question"));

        if (!used.Contains(Email) && (input == "email" || question.Contains("eposta") || question is "email" or "mail" or "e-posta"))
            return Email;
        if (!used.Contains(LastName) && (question.Contains("soyad") || question is "lastname" or "surname"))
            return LastName;
        if (!used.Contains(FirstName) && question is "ad" or "adiniz" or "isim" or "isminiz" or "firstname" or "name")
            return FirstName;
        if (!used.Contains(FirstName) && input == "name")
            return FirstName;
        return null;
    }

    private static FormSchemaItem Clone(FormSchemaItem field) => new()
    {
        Id = field.Id,
        Type = field.Type,
        Props = field.Props is null ? new() : new Dictionary<string, object?>(field.Props),
        Condition = field.Condition is null ? new() : new Dictionary<string, object?>(field.Condition)
    };

    private static string Fold(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return value.Trim().ToLower(new CultureInfo("tr-TR"))
            .Replace("ğ", "g").Replace("ü", "u").Replace("ş", "s")
            .Replace("ı", "i").Replace("ö", "o").Replace("ç", "c")
            .Replace(" ", "").Replace("-", "");
    }

    private static class Prop
    {
        public static string? Get(FormSchemaItem item, string key)
        {
            if (item.Props is null || !item.Props.TryGetValue(key, out var value) || value is null)
                return null;
            if (value is JsonElement el)
            {
                return el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Number => el.GetRawText(),
                    _ => el.ToString()
                };
            }
            if (value is bool flag) return flag ? "true" : "false";
            return value.ToString();
        }
    }
}
