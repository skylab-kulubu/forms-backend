using System.Text.Json;

namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Cevap metnini okumanın tek yeri. Çoklu seçim cevabı istemciye göre JSON dizisi
/// ya da virgülle birleştirilmiş metin olarak gelebildiği için, yönlendirme
/// koşulları, analitik ve dışa aktarım aynı çözümlemeyi paylaşır.
/// </summary>
public static class FormAnswerText
{
    /// <summary>Cevabın içerdiği seçimler. Tek seçimli cevap tek elemanlı listedir.</summary>
    public static IReadOnlyList<string> Selections(string? answer)
    {
        var trimmed = answer?.Trim() ?? string.Empty;

        if (trimmed.Length == 0) return [];

        return TryReadJsonArray(trimmed, out var items)
            ? items
            : trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Cevabın insan tarafından okunacak hali. Yalnız JSON dizisi yeniden biçimlenir:
    /// düz metin olduğu gibi kalmalı, yoksa virgül içeren serbest metin sessizce
    /// değiştirilirdi.
    /// </summary>
    public static string ToDisplayText(string? answer) =>
        TryReadJsonArray(answer?.Trim() ?? string.Empty, out var items)
            ? string.Join(", ", items)
            : answer ?? string.Empty;

    private static bool TryReadJsonArray(string trimmed, out IReadOnlyList<string> items)
    {
        items = [];

        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']')) return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string?>>(trimmed);

            if (parsed is null) return false;

            items = [.. parsed.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim())];
            return true;
        }
        catch (JsonException)
        {
            // Dizi gibi görünen ama geçerli olmayan metin düz metin sayılır.
            return false;
        }
    }
}
