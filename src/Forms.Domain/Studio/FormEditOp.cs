using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Domain.Studio;

public enum FormEditKind
{
    Set,
    Insert,
    Remove,
    Move,
    Replace,
    Reset
}

public sealed record FormEditOp(
    FormEditKind Kind,
    string[] Path,
    JsonNode? Value = null,
    JsonObject? Field = null,
    string? Id = null,
    string? After = null,
    FormDocument? Document = null)
{
    private static readonly string[] FieldParts = ["type", "props", "condition"];

    public static FormEditOp ReplaceWith(FormDocument document) => new(FormEditKind.Replace, [], Document: document);

    public static bool TryReadAll(JsonArray? nodes, [NotNullWhen(true)] out List<FormEditOp>? ops, [NotNullWhen(false)] out string? problem)
    {
        ops = null;

        if (nodes is null || nodes.Count == 0)
        {
            problem = "Gönderilecek değişiklik yok.";
            return false;
        }

        if (nodes.Count > StudioLimits.MaxOpsPerBatch)
        {
            problem = $"Bir seferde en fazla {StudioLimits.MaxOpsPerBatch} değişiklik gönderilebilir.";
            return false;
        }

        var read = new List<FormEditOp>(nodes.Count);

        foreach (var node in nodes)
        {
            if (!TryRead(node, out var op, out problem)) return false;
            read.Add(op);
        }

        ops = read;
        problem = null;
        return true;
    }

    public static bool TryRead(JsonNode? node, [NotNullWhen(true)] out FormEditOp? op, [NotNullWhen(false)] out string? problem)
    {
        op = null;

        if (node is not JsonObject source || !TryKey(source["op"], out var kind, allowReserved: true))
            return Fail(out problem);

        switch (kind)
        {
            case "set":
                if (!TryPath(source["path"], out var path)) return Fail(out problem);
                if (!TryValue(path, source["value"], out var value, out problem)) return false;

                op = new FormEditOp(FormEditKind.Set, path, Value: value);
                break;

            case "insert":
                if (!FormDocumentValues.TryField(source["field"], strict: true, out var field, out problem)) return false;
                if (!TryAnchor(source["after"], out var after)) return Fail(out problem);

                op = new FormEditOp(FormEditKind.Insert, [], Field: field, After: after);
                break;

            case "remove":
                if (!TryKey(source["id"], out var removed)) return Fail(out problem);

                op = new FormEditOp(FormEditKind.Remove, [], Id: removed);
                break;

            case "move":
                if (!TryKey(source["id"], out var moved) || !TryAnchor(source["after"], out var target)) return Fail(out problem);

                op = new FormEditOp(FormEditKind.Move, [], Id: moved, After: target);
                break;

            case "replace":
                if (!FormDocument.TryRead(source["document"], out var document, out problem)) return false;

                op = ReplaceWith(document);
                break;

            case "reset":
                op = new FormEditOp(FormEditKind.Reset, []);
                break;

            default:
                return Fail(out problem);
        }

        problem = null;
        return true;
    }

    public static string Serialize(IEnumerable<FormEditOp> ops) => new JsonArray(ops.Select(op => (JsonNode?)op.ToJson()).ToArray()).ToJsonString();

    public static JsonArray Parse(string json) => JsonNode.Parse(json) as JsonArray ?? new JsonArray();

    public JsonObject ToJson() => Kind switch
    {
        FormEditKind.Set => new JsonObject
        {
            ["op"] = "set",
            ["path"] = new JsonArray(Path.Select(segment => (JsonNode?)JsonValue.Create(segment)).ToArray()),
            ["value"] = Value?.DeepClone()
        },
        FormEditKind.Insert => new JsonObject { ["op"] = "insert", ["field"] = Field!.DeepClone(), ["after"] = After },
        FormEditKind.Remove => new JsonObject { ["op"] = "remove", ["id"] = Id },
        FormEditKind.Move => new JsonObject { ["op"] = "move", ["id"] = Id, ["after"] = After },
        FormEditKind.Replace => new JsonObject { ["op"] = "replace", ["document"] = Document!.ToJson() },
        _ => new JsonObject { ["op"] = "reset" }
    };

    private static bool TryPath(JsonNode? node, [NotNullWhen(true)] out string[]? path)
    {
        path = null;

        if (node is not JsonArray parts || parts.Count is not (1 or 3 or 4)) return false;

        var segments = new string[parts.Count];

        for (var index = 0; index < parts.Count; index++)
        {
            if (parts[index] is not JsonValue part || part.GetValueKind() != JsonValueKind.String || !part.TryGetValue<string>(out var segment))
                return false;

            segments[index] = segment;
        }

        var valid = segments switch
        {
            [var key] => FormDocumentValues.IsSettingKey(key),
            ["schema", var id, var part] => FormDocumentValues.IsKey(id) && FieldParts.Contains(part, StringComparer.Ordinal),
            ["schema", var id, "props", var prop] => FormDocumentValues.IsKey(id) && FormDocumentValues.IsKey(prop),
            _ => false
        };

        if (!valid) return false;

        path = segments;
        return true;
    }

    private static bool TryValue(string[] path, JsonNode? value, out JsonNode? normalized, [NotNullWhen(false)] out string? problem)
    {
        switch (path)
        {
            case [var key]:
                return FormDocumentValues.TrySetting(key, value, out normalized, out problem);

            case [_, _, "type"]:
                return FormDocumentValues.TryType(value, out normalized, out problem);

            case [_, _, "props"]:
                var ok = FormDocumentValues.TryProps(value, strict: true, out var props, out problem);
                normalized = props;
                return ok;

            case [_, _, "condition"]:
                var read = FormDocumentValues.TryCondition(value, out var condition, out problem);
                normalized = condition;
                return read;

            default:
                normalized = value?.DeepClone();
                problem = null;
                return true;
        }
    }

    private static bool TryAnchor(JsonNode? node, out string? after)
    {
        after = null;
        return node is null || TryKey(node, out after);
    }

    private static bool TryKey(JsonNode? node, [NotNullWhen(true)] out string? key, bool allowReserved = false)
    {
        key = null;

        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String || !value.TryGetValue<string>(out var text))
            return false;

        if (!allowReserved && !FormDocumentValues.IsKey(text)) return false;

        key = text;
        return true;
    }

    private static bool Fail([NotNullWhen(false)] out string? problem)
    {
        problem = "Değişiklik okunamadı.";
        return false;
    }
}
