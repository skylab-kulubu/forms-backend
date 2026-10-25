using System.Text.Json;
using System.Text.Json.Nodes;

namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Bir form şemasının iki hâlini anlama göre karşılaştırır. Postgres jsonb nesne anahtarlarını kendi sırasına
/// dizdiği için kayıtlı şema ile istemcinin gönderdiği aynı şema metin olarak farklı çıkabilir.
/// </summary>
public static class FormSchemaChange
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static bool IsSame(IReadOnlyList<FormSchemaItem>? left, IReadOnlyList<FormSchemaItem>? right) =>
        JsonNode.DeepEquals(ToNode(left), ToNode(right));

    /// <summary>
    /// Yarım cevap yalnız sorunun türü değişince bozulur. Metin düzeltmesi cevabı geçersiz kılmaz; silinen sorunun
    /// cevabı da gönderimde ve dosya tutmalarında zaten şemaya göre düşer.
    /// </summary>
    public static bool BreaksDrafts(IEnumerable<FormSchemaItem> before, IEnumerable<FormSchemaItem> after)
    {
        // İstemci id'yi null gönderebilir; karşılaştırma bu yüzden 500'e dönmesin.
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in after) types.TryAdd(item.Id ?? string.Empty, item.Type);

        return before.Any(item => types.TryGetValue(item.Id ?? string.Empty, out var type) && !string.Equals(type, item.Type, StringComparison.Ordinal));
    }

    private static JsonNode? ToNode(IReadOnlyList<FormSchemaItem>? schema) =>
        JsonNode.Parse(JsonSerializer.Serialize(schema, CamelCase));
}
