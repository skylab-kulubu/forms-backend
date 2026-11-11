using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Domain.Studio;

public sealed class FormDocument
{
    public const string UntitledTitle = "Adsız form";

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly JsonObject _root;

    private FormDocument(JsonObject root) => _root = root;

    public string Title => (string?)_root["title"] ?? "";

    public string? Description => (string?)_root["description"];

    public FormStatus Status => (FormStatus)((int?)_root["status"] ?? (int)FormStatus.Closed);

    public bool AllowAnonymousResponses => (bool?)_root["allowAnonymousResponses"] ?? false;

    public bool AllowMultipleResponses => (bool?)_root["allowMultipleResponses"] ?? false;

    public bool RequiresManualReview => (bool?)_root["requiresManualReview"] ?? false;

    public FormTask? Task => _root["task"]?.Deserialize<FormTask>(CamelCase);

    public DateTime? ClosesAt => FormDocumentValues.ReadDate((string?)_root["closesAt"]);

    public int? TimeLimitMinutes => (int?)_root["timeLimitMinutes"];

    public string? Brand => (string?)_root["brand"];

    public List<FormSchemaItem> Schema => Fields.Deserialize<List<FormSchemaItem>>(CamelCase) ?? [];

    public int ByteSize => Encoding.UTF8.GetByteCount(ToJsonString());

    private JsonArray Fields => _root["schema"]!.AsArray();

    public static FormDocument Of(Form form)
    {
        var source = new JsonObject
        {
            ["title"] = form.Title,
            ["description"] = form.Description,
            ["schema"] = JsonSerializer.SerializeToNode(form.Schema, CamelCase),
            ["status"] = (int)(form.Status == FormStatus.Open ? FormStatus.Open : FormStatus.Closed),
            ["allowAnonymousResponses"] = form.AllowAnonymousResponses,
            ["allowMultipleResponses"] = form.AllowMultipleResponses,
            ["requiresManualReview"] = form.RequiresManualReview,
            ["task"] = form.Task is null ? null : JsonSerializer.SerializeToNode(form.Task, CamelCase),
            ["closesAt"] = form.ClosesAt is { } closesAt ? FormDocumentValues.WriteDate(closesAt) : null,
            ["timeLimitMinutes"] = form.TimeLimitMinutes,
            ["brand"] = form.Brand
        };

        return Read(source, strict: false, out var document, out var problem) ? document : throw new InvalidOperationException(problem);
    }

    public static FormDocument FromStored(string json) =>
        Read(JsonNode.Parse(json), strict: false, out var document, out var problem) ? document : throw new InvalidOperationException(problem);

    public static bool TryRead(JsonNode? node, [NotNullWhen(true)] out FormDocument? document, [NotNullWhen(false)] out string? problem) =>
        Read(node, strict: true, out document, out problem);

    public JsonObject ToJson() => (JsonObject)_root.DeepClone();

    public string ToJsonString() => _root.ToJsonString();

    public FormDocument Clone() => new(ToJson());

    public bool HasField(string id) => IndexOf(id) >= 0;

    public string? TypeOf(string id) => (string?)FieldOf(id)?["type"];

    public FormDocument Publishable()
    {
        var root = ToJson();

        if (string.IsNullOrWhiteSpace((string?)root["title"])) root["title"] = UntitledTitle;
        if (string.IsNullOrEmpty((string?)root["description"])) root["description"] = null;

        if ((bool?)root["allowAnonymousResponses"] == true)
        {
            root["allowMultipleResponses"] = true;
            root["timeLimitMinutes"] = null;
        }

        if ((int?)root["timeLimitMinutes"] is <= 0) root["timeLimitMinutes"] = null;
        if (root["task"] is JsonObject task && string.IsNullOrWhiteSpace((string?)task["content"])) root["task"] = null;

        return new FormDocument(root);
    }

    public bool SameAs(FormDocument other) => JsonNode.DeepEquals(Publishable()._root, other.Publishable()._root);

    internal bool TryApply(FormEditOp op) => op.Kind switch
    {
        FormEditKind.Set => TrySet(op.Path, op.Value),
        FormEditKind.Insert => TryInsert(op.Field!, op.After),
        FormEditKind.Remove => TryRemove(op.Id!),
        FormEditKind.Move => TryMove(op.Id!, op.After),
        FormEditKind.Replace => Replace(op.Document!),
        _ => false
    };

    private bool TrySet(string[] path, JsonNode? value)
    {
        if (path.Length == 1)
        {
            _root[path[0]] = value?.DeepClone();
            return true;
        }

        if (FieldOf(path[1]) is not { } field) return false;

        if (path.Length == 3)
        {
            if (value is null) field.Remove(path[2]);
            else field[path[2]] = value.DeepClone();

            return true;
        }

        var props = field["props"] as JsonObject ?? (JsonObject)(field["props"] = new JsonObject());

        if (value is null) props.Remove(path[3]);
        else props[path[3]] = value.DeepClone();

        return true;
    }

    private bool TryInsert(JsonObject field, string? after)
    {
        if (HasField((string)field["id"]!)) return false;

        var anchor = after is null ? -1 : IndexOf(after);
        var at = after is null ? 0 : anchor >= 0 ? anchor + 1 : Fields.Count;

        Fields.Insert(at, field.DeepClone());
        return true;
    }

    private bool TryRemove(string id)
    {
        var index = IndexOf(id);
        if (index < 0) return false;

        Fields.RemoveAt(index);
        return true;
    }

    private bool TryMove(string id, string? after)
    {
        var from = IndexOf(id);
        if (from < 0 || id == after || after is not null && IndexOf(after) < 0) return false;

        var field = Fields[from];
        Fields.RemoveAt(from);
        Fields.Insert(after is null ? 0 : IndexOf(after) + 1, field);

        return true;
    }

    private bool Replace(FormDocument document)
    {
        _root.Clear();
        foreach (var (key, value) in document.ToJson()) _root[key] = value?.DeepClone();

        return true;
    }

    private JsonObject? FieldOf(string id)
    {
        var index = IndexOf(id);
        return index < 0 ? null : (JsonObject)Fields[index]!;
    }

    private int IndexOf(string id)
    {
        var fields = Fields;

        for (var index = 0; index < fields.Count; index++)
            if (fields[index] is JsonObject field && (string?)field["id"] == id) return index;

        return -1;
    }

    private static bool Read(JsonNode? node, bool strict, [NotNullWhen(true)] out FormDocument? document, [NotNullWhen(false)] out string? problem)
    {
        document = null;

        if (node is not JsonObject source)
        {
            problem = "Form belgesi okunamadı.";
            return false;
        }

        if (!FormDocumentValues.TrySchema(source["schema"], strict, out var schema, out problem)) return false;

        var root = new JsonObject();

        foreach (var key in FormDocumentValues.SettingKeys)
        {
            var value = FormDocumentValues.DefaultOf(key);

            if (source.ContainsKey(key) && !FormDocumentValues.TrySetting(key, source[key], out value, out problem)) return false;

            root[key] = value;
            if (key == "description") root["schema"] = schema;
        }

        document = new FormDocument(root);
        problem = null;
        return true;
    }
}
