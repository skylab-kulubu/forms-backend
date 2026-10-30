using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skylab.Forms.Domain.Models;

public static class FormSchemaChange
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static bool IsSame(IReadOnlyList<FormSchemaItem>? left, IReadOnlyList<FormSchemaItem>? right) =>
        JsonNode.DeepEquals(ToNode(left), ToNode(right));

    public static bool BreaksDrafts(IEnumerable<FormSchemaItem> before, IEnumerable<FormSchemaItem> after)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in after) types.TryAdd(item.Id ?? string.Empty, item.Type);

        return before.Any(item => types.TryGetValue(item.Id ?? string.Empty, out var type) && !string.Equals(type, item.Type, StringComparison.Ordinal));
    }

    private static JsonNode? ToNode(IReadOnlyList<FormSchemaItem>? schema) =>
        JsonNode.Parse(JsonSerializer.Serialize(schema, CamelCase));
}
