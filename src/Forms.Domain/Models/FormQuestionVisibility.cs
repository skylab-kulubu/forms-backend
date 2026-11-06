using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Skylab.Forms.Domain.Models;

public static partial class FormQuestionVisibility
{
    public static bool IsShown(FormSchemaItem question, IReadOnlyList<FormSchemaItem> schema, IReadOnlyDictionary<string, string?> answers)
    {
        var fieldId = ConditionText(question, "fieldId");

        if (string.IsNullOrEmpty(fieldId)) return true;

        var source = schema.FirstOrDefault(item => item.Id == fieldId);
        var answer = answers.GetValueOrDefault(fieldId)?.Trim() ?? string.Empty;
        var comparison = ConditionText(question, "operator");
        var target = ConditionText(question, "value") ?? string.Empty;
        var blank = answer.Length == 0 || (source?.Type == "matrix" && IsBlankMatrix(answer));

        if (comparison == "is_set") return !blank;
        if (comparison == "is_empty") return blank;
        if (blank) return false;

        return comparison switch
        {
            "is_true" => Same(answer, LabelOf(source, "trueLabel", "Evet")),
            "is_false" => Same(answer, LabelOf(source, "falseLabel", "Hayır")),
            _ when IsNumeric(source) => CompareNumbers(answer, target, comparison),
            _ when source?.Type == "multi_choice" => CompareSelections(answer, target, comparison),
            _ when DateOrTime().IsMatch(answer) => CompareMoments(answer, target, comparison),
            _ => CompareText(answer, target, comparison)
        };
    }

    private static bool IsBlankMatrix(string answer)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(answer)?.Values.All(string.IsNullOrWhiteSpace) ?? true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ConditionText(FormSchemaItem question, string key) =>
        question.Condition?.TryGetValue(key, out var value) == true ? value?.ToString() : null;

    private static string? PropText(FormSchemaItem? question, string key) =>
        question?.Props?.TryGetValue(key, out var value) == true ? value?.ToString() : null;

    private static string LabelOf(FormSchemaItem? source, string key, string fallback) =>
        PropText(source, key) is { Length: > 0 } label ? label : fallback;

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsNumeric(FormSchemaItem? source) =>
        source?.Type == "slider" || PropText(source, "inputType") == "number";

    private static bool CompareNumbers(string answer, string target, string? comparison)
    {
        var parsed = double.TryParse(answer, NumberStyles.Float, CultureInfo.InvariantCulture, out var left);
        var wanted = double.TryParse(target, NumberStyles.Float, CultureInfo.InvariantCulture, out var right);

        if (!parsed || !wanted) return comparison == "not_equals";

        return comparison switch
        {
            "equals" => left == right,
            "not_equals" => left != right,
            "greater_than" => left > right,
            "less_than" => left < right,
            _ => false
        };
    }

    private static bool CompareSelections(string answer, string target, string? comparison)
    {
        var selected = FormAnswerText.Selections(answer).Contains(target, StringComparer.Ordinal);

        return comparison switch
        {
            "equals" => selected,
            "not_equals" => !selected,
            _ => false
        };
    }

    private static bool CompareMoments(string answer, string target, string? comparison) => comparison switch
    {
        "equals" => answer == target,
        "not_equals" => answer != target,
        "before" => string.CompareOrdinal(answer, target) < 0,
        "after" => string.CompareOrdinal(answer, target) > 0,
        _ => false
    };

    private static bool CompareText(string answer, string target, string? comparison) => comparison switch
    {
        "equals" => Same(answer, target),
        "not_equals" => !Same(answer, target),
        "contains" => answer.Contains(target, StringComparison.OrdinalIgnoreCase),
        "starts_with" => answer.StartsWith(target, StringComparison.OrdinalIgnoreCase),
        "ends_with" => answer.EndsWith(target, StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}|\d{2}:\d{2})$")]
    private static partial Regex DateOrTime();
}
