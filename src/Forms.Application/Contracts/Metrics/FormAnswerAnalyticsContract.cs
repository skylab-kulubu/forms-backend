namespace Skylab.Forms.Application.Contracts.Metrics;

public enum AnalyticsKind
{
    None = 0,        // unknown type: no distribution
    Text = 1,        // short_text, long_text, link: free text, not aggregated
    File = 2,        // file upload: only answered/skipped counts
    Choice = 3,      // combobox: single choice
    MultiChoice = 4, // multi_choice: several selections, as a JSON array or comma-joined
    Switch = 5,      // toggle: localized Evet/Hayır
    Number = 6,      // slider: numeric
    Matrix = 7,      // matrix: per-row column distribution
    Date = 8,        // date: monthly histogram
    Time = 9,        // time: hour-of-day histogram
    Repeater = 10    // repeater: repeated group, only answered/skipped counts
}

public record FormAnswerAnalyticsContract(
    Guid FormId,
    int TotalResponses,
    DateTime GeneratedAt,
    List<QuestionAnalyticsContract> Questions
);

public record QuestionAnalyticsContract(
    string QuestionId,
    string Question,
    string Type,
    AnalyticsKind Kind,
    bool Aggregatable,
    int AnsweredCount,
    int SkippedCount,
    List<AnswerBucketContract>? Distribution,
    NumericSummaryContract? Numeric,
    DateRangeContract? DateRange,
    List<MatrixRowAnalyticsContract>? Rows
);

public record AnswerBucketContract(string Value, int Count, double Percentage);

public record NumericSummaryContract(double Average, double Min, double Max, double Median, int Count);

public record DateRangeContract(DateOnly Earliest, DateOnly Latest);

public record MatrixRowAnalyticsContract(string Row, int AnsweredCount, List<AnswerBucketContract> Distribution);
