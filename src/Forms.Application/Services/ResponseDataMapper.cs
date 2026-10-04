using System.Text.Json;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services;

public static class ResponseDataMapper
{
    public static List<FormResponseSchemaItem> Map(IEnumerable<FormSchemaItem> schema, IReadOnlyList<FormResponseSchemaItem> answers) =>
        [.. schema.Select(item => new FormResponseSchemaItem
        {
            Id = item.Id,
            Type = item.Type,
            Question = QuestionOf(item),
            Answer = answers.FirstOrDefault(answer => answer.Id == item.Id)?.Answer ?? string.Empty
        })];

    public static string QuestionOf(FormSchemaItem item) =>
        item.Props.TryGetValue("question", out var value) && value != null ? value.ToString() ?? string.Empty : string.Empty;

    public static bool IsRequired(FormSchemaItem item) =>
        item.Props.TryGetValue("required", out var value) && value switch
        {
            JsonElement element => element.ValueKind == JsonValueKind.True,
            bool flag => flag,
            _ => false
        };

    public static List<FormResponseSchemaItem> FromDraft(IEnumerable<FormResponseSchemaItem> draft) =>
        [.. draft.Select(item => new FormResponseSchemaItem
        {
            Id = item.Id,
            Type = item.Type,
            Question = item.Question,
            Answer = DisplayText(item.Answer)
        })];

    private static string DisplayText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        try
        {
            using var document = JsonDocument.Parse(raw);
            return Text(document.RootElement);
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string Text(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.True => "Evet",
        JsonValueKind.False => "Hayır",
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(Text).Where(text => text.Length > 0)),
        _ => element.GetRawText()
    };
}
