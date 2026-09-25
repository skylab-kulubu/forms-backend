namespace Skylab.Forms.Application.Contracts.Metrics;

public record FormMetricsContract(
    int TotalResponses,
    int PendingCount,
    int ApprovedCount,
    int RejectedCount,
    double? AverageCompletionTime,
    double DailyTrendPercentage,
    double HourlyTrendPercentage,
    SourceBreakdownContract SourceBreakdown,
    List<TrendItemContract> DailyTrend,
    List<TrendItemContract> HourlyTrend,
    List<ChannelMetricContract>? Channels = null
);

/// <param name="Source">Normalize edilmiş utm_source; etiketsiz gelenler için null.</param>
/// <param name="Clicks">Kısa linkin core'daki açılışları; core'a ulaşılamadıysa null.</param>
public record ChannelMetricContract(string? Source, int Responses, int? Clicks);

public record ServiceMetricsContract(
    int TotalForms,
    int TotalResponsesReceived,
    int PendingResponsesCount,
    double FormsWeeklyTrendPercentage,
    double ResponsesWeeklyTrendPercentage,
    List<TrendItemContract> FormsCreatedWeeklyTrend,
    List<TrendItemContract> ResponsesWeeklyTrend
);

public record SourceBreakdownContract(int Registered, int Anonymous);
public record TrendItemContract(string Key, string Label, int Count);
