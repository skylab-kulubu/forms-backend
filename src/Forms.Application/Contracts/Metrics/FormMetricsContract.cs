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
    List<ChannelMetricContract>? Channels = null,
    double? MedianCompletionTime = null,
    int FlaggedCount = 0,
    FormReviewMetricsContract? Review = null,
    FormLifetimeContract? Lifetime = null
);

/// <param name="Source">Normalize edilmiş utm_source; etiketsiz gelenler için null.</param>
/// <param name="Clicks">Kısa linkin core'daki açılışları; core'a ulaşılamadıysa null.</param>
/// <param name="Scans">Açılışların QR okutmasıyla gelen kısmı; core'a ulaşılamadıysa null.</param>
public record ChannelMetricContract(
    string? Source,
    int Responses,
    int? Clicks,
    int? Scans = null,
    int Approved = 0,
    int Declined = 0,
    List<ChannelCampaignContract>? Campaigns = null
);

public record ChannelCampaignContract(string? Campaign, int Responses, int Approved, int Declined);

public record FormReviewMetricsContract(
    int DecidedCount,
    double? MedianDecisionSeconds,
    OldestPendingResponseContract? OldestPending,
    List<FormReviewerContract> Reviewers
);

public record OldestPendingResponseContract(Guid ResponseId, DateTime SubmittedAt);

public record FormReviewerContract(Guid UserId, int Decisions);

public record FormLifetimeContract(DateTime? ClosesAt, List<FormLifetimeDayContract> Days);

public record FormLifetimeDayContract(DateOnly Date, int Count);

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
