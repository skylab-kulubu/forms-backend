namespace Forms.Application.Contracts.Metrics;

public record FormMetricsContract(
    int TotalResponses,
    int PendingCount,
    int ApprovedCount,
    int RejectedCount,
    double? AverageCompletionTime,
    SourceBreakdownContract SourceBreakdown,
    List<TrendItemContract> DailyTrend,
    List<TrendItemContract> HourlyTrend
);

public record SourceBreakdownContract(int Registered, int Anonymous);
public record TrendItemContract(string Key, string Label, int Count);