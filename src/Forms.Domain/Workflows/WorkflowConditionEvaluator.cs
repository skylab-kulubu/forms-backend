using System.Globalization;
using System.Text.Json;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Domain.Workflows;

/// <summary>
/// Koşul kurallarını cevap anlık görüntüsüne karşı değerlendirir. Hiçbir kural
/// istisna fırlatmaz: okunamayan veya çevrilemeyen cevap kuralı düşürür, çünkü
/// rota seçimi kullanıcı girdisiyle çökmemelidir.
/// </summary>
public static class WorkflowConditionEvaluator
{
    public static bool Matches(WorkflowConditionGroup? condition, WorkflowEvaluationContext context)
    {
        if (condition is null || condition.Rules.Count == 0) return true;

        return condition.Operator == WorkflowConditionOperator.Any
            ? condition.Rules.Any(rule => MatchesRule(rule, context))
            : condition.Rules.All(rule => MatchesRule(rule, context));
    }

    private static bool MatchesRule(WorkflowConditionRule rule, WorkflowEvaluationContext context)
    {
        var answer = context.AnswersFor(rule.NodeKey)
            .FirstOrDefault(item => item.Id == rule.QuestionId)?.Answer;

        return rule.Comparison switch
        {
            WorkflowConditionComparison.IsEmpty => string.IsNullOrWhiteSpace(answer),
            WorkflowConditionComparison.IsNotEmpty => !string.IsNullOrWhiteSpace(answer),

            WorkflowConditionComparison.Equals => TextEquals(answer, rule.Value),
            WorkflowConditionComparison.NotEquals => !TextEquals(answer, rule.Value),

            WorkflowConditionComparison.In => SharesValue(answer, rule.Values),
            WorkflowConditionComparison.NotIn => !SharesValue(answer, rule.Values),

            WorkflowConditionComparison.Contains => ContainsText(answer, rule.Value),

            WorkflowConditionComparison.GreaterThan => CompareNumbers(answer, rule.Value, result => result > 0),
            WorkflowConditionComparison.GreaterThanOrEqual => CompareNumbers(answer, rule.Value, result => result >= 0),
            WorkflowConditionComparison.LessThan => CompareNumbers(answer, rule.Value, result => result < 0),
            WorkflowConditionComparison.LessThanOrEqual => CompareNumbers(answer, rule.Value, result => result <= 0),

            _ => false
        };
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    // Ordinal karşılaştırma bilinçli: kültüre duyarlı karşılaştırma Türkçe i/I
    // kuralları yüzünden aynı tanımı farklı sunucularda farklı yorumlayabilirdi.
    private static bool TextEquals(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsText(string? answer, string? value)
    {
        var needle = Normalize(value);
        if (needle.Length == 0) return false;

        return Normalize(answer).Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Çoklu seçimde "seçilenlerden herhangi biri listede mi" anlamına gelir.</summary>
    private static bool SharesValue(string? answer, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0) return false;

        var selected = SplitAnswer(answer);
        if (selected.Count == 0) return false;

        return selected.Any(item => values.Any(value => TextEquals(item, value)));
    }

    /// <summary>
    /// Çoklu seçim cevabının JSON dizisi mi yoksa virgülle ayrılmış metin mi
    /// olduğu istemciye göre değişebildiği için ikisi de kabul edilir.
    /// </summary>
    private static IReadOnlyList<string> SplitAnswer(string? answer)
    {
        var trimmed = Normalize(answer);
        if (trimmed.Length == 0) return Array.Empty<string>();

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string?>>(trimmed);

                if (parsed is not null)
                    return parsed.Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value!.Trim())
                        .ToList();
            }
            catch (JsonException)
            {
                // Dizi gibi görünen ama geçerli olmayan metin düz metin sayılır.
            }
        }

        return trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool CompareNumbers(string? answer, string? value, Func<int, bool> matches)
        => TryParseNumber(answer, out var left) && TryParseNumber(value, out var right) && matches(left.CompareTo(right));

    private static bool TryParseNumber(string? text, out decimal value)
    {
        var normalized = Normalize(text);

        // NumberStyles.Float binlik ayırıcıyı kabul etmez: "3,5" sessizce 35 olarak
        // okunmasın diye bu bilinçli bir tercih.
        if (decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;

        // Virgüllü ondalık yazım yalnız tek virgül ve hiç nokta yokken tekanlamlıdır.
        if (normalized.Count(character => character == ',') == 1 && !normalized.Contains('.'))
            return decimal.TryParse(normalized.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        value = 0m;
        return false;
    }
}
