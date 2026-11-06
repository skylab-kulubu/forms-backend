using System.Globalization;
using System.Text.Json;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Metrics;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services;
public static class AnswerAnalyticsBuilder
{
    private static readonly Dictionary<string, AnalyticsKind> KindByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["combobox"] = AnalyticsKind.Choice,
        ["multi_choice"] = AnalyticsKind.MultiChoice,
        ["toggle"] = AnalyticsKind.Switch,
        ["slider"] = AnalyticsKind.Number,
        ["matrix"] = AnalyticsKind.Matrix,
        ["date"] = AnalyticsKind.Date,
        ["time"] = AnalyticsKind.Time,
        ["short_text"] = AnalyticsKind.Text,
        ["long_text"] = AnalyticsKind.Text,
        // Eski şemalar link'i ayrı bir tip olarak taşıyor; yeni formlarda short_text
        // altında inputType olarak duruyor. Giriş kalmalı, yoksa o sorular bilinmeyen
        // tipe düşer ve analitikte tamamen kaybolur.
        ["link"] = AnalyticsKind.Text,
        ["file"] = AnalyticsKind.File,
        ["repeater"] = AnalyticsKind.Repeater,
    };

    public static FormAnswerAnalyticsContract Build(Form form, IReadOnlyCollection<ResponseAnswers> responses, DateTime generatedAt)
    {
        var answersById = new Dictionary<string, List<GivenAnswer>>();
        var embeddedTextById = new Dictionary<string, string>();

        var questionsById = form.Schema.ToDictionary(item => item.Id);

        foreach (var response in responses)
        {
            var answers = new Dictionary<string, string?>();
            foreach (var item in response.Data)
                answers.TryAdd(item.Id, item.Answer);

            var seen = new HashSet<string>();
            foreach (var item in response.Data)
            {
                if (!seen.Add(item.Id))
                    continue;

                if (!embeddedTextById.ContainsKey(item.Id) && !string.IsNullOrWhiteSpace(item.Question))
                    embeddedTextById[item.Id] = item.Question;

                if (string.IsNullOrWhiteSpace(item.Answer)
                    && questionsById.TryGetValue(item.Id, out var question)
                    && !FormQuestionVisibility.IsShown(question, form.Schema, answers))
                    continue;

                if (!answersById.TryGetValue(item.Id, out var list))
                    answersById[item.Id] = list = new List<GivenAnswer>();
                list.Add(new GivenAnswer(item.Answer, response.Status));
            }
        }

        var questions = form.Schema.Select(schemaItem => BuildQuestion(schemaItem, answersById, embeddedTextById)).ToList();

        var decisions = new DecisionCountsContract(
            responses.Count(response => response.Status == FormResponseStatus.Approved),
            responses.Count(response => response.Status == FormResponseStatus.Declined));

        return new FormAnswerAnalyticsContract(form.Id, responses.Count, generatedAt, questions, decisions);
    }

    public static Func<IReadOnlyList<FormResponseSchemaItem>, bool> GaveAnswer(Form form, string questionId, string? value)
    {
        var wanted = value?.Trim() ?? string.Empty;
        var question = form.Schema.FirstOrDefault(item => item.Id == questionId);
        var multiple = question is not null && KindOf(question) == AnalyticsKind.MultiChoice;

        return data =>
            wanted.Length > 0
            && data.FirstOrDefault(item => item.Id == questionId)?.Answer is { } answer
            && (multiple ? FormAnswerText.Selections(answer).Contains(wanted, StringComparer.Ordinal) : answer.Trim() == wanted);
    }

    private static AnalyticsKind KindOf(FormSchemaItem schemaItem)
    {
        if (string.Equals(schemaItem.Type, "short_text", StringComparison.OrdinalIgnoreCase)
            && schemaItem.Props.TryGetValue("inputType", out var inputType)
            && inputType?.ToString() == "number")
            return AnalyticsKind.Number;

        return KindByType.TryGetValue(schemaItem.Type ?? "", out var kind) ? kind : AnalyticsKind.None;
    }

    private static QuestionAnalyticsContract BuildQuestion(
        FormSchemaItem schemaItem,
        Dictionary<string, List<GivenAnswer>> answersById,
        Dictionary<string, string> embeddedTextById)
    {
        var kind = KindOf(schemaItem);
        var text = ResolveQuestionText(schemaItem, embeddedTextById);

        var raw = answersById.TryGetValue(schemaItem.Id, out var list) ? list : new List<GivenAnswer>();
        var given = raw
            .Where(answer => !string.IsNullOrWhiteSpace(answer.Text))
            .Select(answer => answer with { Text = answer.Text!.Trim() })
            .ToList();
        var answered = given.Select(answer => answer.Text!).ToList();
        var answeredCount = answered.Count;
        var askedCount = raw.Count;

        List<AnswerBucketContract>? distribution = null;
        NumericSummaryContract? numeric = null;
        DateRangeContract? dateRange = null;
        List<MatrixRowAnalyticsContract>? rows = null;

        switch (kind)
        {
            case AnalyticsKind.Choice:
            case AnalyticsKind.Switch:
                distribution = ToDecidedBuckets(given, answeredCount);
                break;

            case AnalyticsKind.MultiChoice:
                var options = given.SelectMany(answer => FormAnswerText.Selections(answer.Text).Select(option => answer with { Text = option }));
                distribution = ToDecidedBuckets(options, answeredCount);
                break;

            case AnalyticsKind.Number:
                distribution = ToDecidedBuckets(given, answeredCount);
                numeric = BuildNumericSummary(answered);
                break;

            case AnalyticsKind.Date:
                (distribution, dateRange) = BuildDate(answered);
                break;

            case AnalyticsKind.Time:
                distribution = BuildTime(answered);
                break;

            case AnalyticsKind.Matrix:
                (rows, answeredCount) = BuildMatrix(answered);
                break;
        }

        var aggregatable = kind is not (AnalyticsKind.None or AnalyticsKind.Text or AnalyticsKind.File or AnalyticsKind.Repeater);

        return new QuestionAnalyticsContract(
            schemaItem.Id, text, schemaItem.Type ?? "", kind, aggregatable,
            answeredCount, Math.Max(0, askedCount - answeredCount), askedCount, distribution, numeric, dateRange, rows);
    }

    private static string ResolveQuestionText(FormSchemaItem schemaItem, Dictionary<string, string> embeddedTextById)
    {
        if (schemaItem.Props.TryGetValue("question", out var q) && q?.ToString() is { Length: > 0 } fromSchema)
            return fromSchema;
        if (embeddedTextById.TryGetValue(schemaItem.Id, out var fromResponse))
            return fromResponse;
        return schemaItem.Id;
    }

    private static List<AnswerBucketContract> ToBuckets(IEnumerable<string> values, int denom) =>
        values
            .GroupBy(v => v)
            .Select(g => new AnswerBucketContract(g.Key, g.Count(), Percentage(g.Count(), denom)))
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Value, StringComparer.Ordinal)
            .ToList();

    private static List<AnswerBucketContract> ToDecidedBuckets(IEnumerable<GivenAnswer> answers, int denom) =>
        answers
            .GroupBy(answer => answer.Text!)
            .Select(group => new AnswerBucketContract(
                group.Key,
                group.Count(),
                Percentage(group.Count(), denom),
                group.Count(answer => answer.Status == FormResponseStatus.Approved),
                group.Count(answer => answer.Status == FormResponseStatus.Declined)))
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Value, StringComparer.Ordinal)
            .ToList();

    private static NumericSummaryContract? BuildNumericSummary(List<string> answered)
    {
        var numbers = answered
            // Float (not Any): reject group separators so a comma-decimal like "1,5" is not misread as 1500/15.
            .Select(a => double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (double?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .OrderBy(n => n)
            .ToList();

        if (numbers.Count == 0) return null;

        return new NumericSummaryContract(
            Math.Round(numbers.Average(), 2), numbers[0], numbers[^1], Statistics.Median(numbers)!.Value, numbers.Count);
    }

    private static (List<AnswerBucketContract> Distribution, DateRangeContract? Range) BuildDate(List<string> answered)
    {
        var dates = answered
            .Select(a => DateOnly.TryParseExact(a, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateOnly?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        if (dates.Count == 0)
            return (new List<AnswerBucketContract>(), null);

        // Monthly histogram, ordered chronologically rather than by count.
        var buckets = dates
            .GroupBy(d => new DateOnly(d.Year, d.Month, 1))
            .OrderBy(g => g.Key)
            // Denominator is answeredCount (not parsed count) so buckets stay consistent with
            // AnsweredCount; unparseable values show up as the sub-100% remainder.
            .Select(g => new AnswerBucketContract(g.Key.ToString("yyyy-MM"), g.Count(), Percentage(g.Count(), answered.Count)))
            .ToList();

        return (buckets, new DateRangeContract(dates.Min(), dates.Max()));
    }

    private static List<AnswerBucketContract> BuildTime(List<string> answered)
    {
        var hours = answered
            .Select(a => TimeOnly.TryParseExact(a, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.Hour : (int?)null)
            .Where(h => h.HasValue)
            .Select(h => h!.Value)
            .ToList();

        return hours
            .GroupBy(h => h)
            .OrderBy(g => g.Key)
            .Select(g => new AnswerBucketContract($"{g.Key:D2}:00", g.Count(), Percentage(g.Count(), answered.Count)))
            .ToList();
    }

    private static (List<MatrixRowAnalyticsContract> Rows, int AnsweredCount) BuildMatrix(List<string> answered)
    {
        var rowOrder = new List<string>();
        var columnsByRow = new Dictionary<string, List<string>>();
        var answeredByRow = new Dictionary<string, int>();
        var answeredCount = 0;

        foreach (var answer in answered)
        {
            Dictionary<string, string>? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(answer);
            }
            catch (JsonException)
            {
                continue;
            }

            if (parsed is null || parsed.Count == 0) continue;
            answeredCount++;

            foreach (var (row, column) in parsed)
            {
                if (string.IsNullOrWhiteSpace(column)) continue;

                if (!columnsByRow.TryGetValue(row, out var cols))
                {
                    columnsByRow[row] = cols = new List<string>();
                    answeredByRow[row] = 0;
                    rowOrder.Add(row);
                }
                cols.Add(column.Trim());
                answeredByRow[row]++;
            }
        }

        var rows = rowOrder
            .Select(row => new MatrixRowAnalyticsContract(row, answeredByRow[row], ToBuckets(columnsByRow[row], answeredByRow[row])))
            .ToList();

        return (rows, answeredCount);
    }

    private static double Percentage(int count, int denom) =>
        denom <= 0 ? 0 : Math.Round((double)count / denom * 100, 2);

    private readonly record struct GivenAnswer(string? Text, FormResponseStatus Status);
}
