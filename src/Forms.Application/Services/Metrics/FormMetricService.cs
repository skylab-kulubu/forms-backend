using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.Metrics;

namespace Skylab.Forms.Application.Services;

public class FormMetricService : IFormMetricService
{
    // Short safety TTL only: explicit invalidation (submit/archive/schema change) handles the
    // common case. Keeping this small also bounds staleness from a rare read/invalidate race
    // (a request that reads the DB just before a concurrent write repopulates the old snapshot).
    private static readonly TimeSpan AnalyticsCacheTtl = TimeSpan.FromSeconds(60);

    private readonly IFormRepository _forms;
    private readonly IFormMetricsRepository _metrics;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cache;

    public FormMetricService(IFormRepository forms, IFormMetricsRepository metrics, ICurrentUserService currentUserService, ICacheService cache)
    {
        _forms = forms;
        _metrics = metrics;
        _currentUserService = currentUserService;
        _cache = cache;
    }

    public async Task<ServiceResult<FormAnswerAnalyticsContract>> GetAnswerAnalyticsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _forms.ExistsAsync(formId, cancellationToken))
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken)
            && !await _forms.IsUserCollaboratorAsync(formId, userId, cancellationToken))
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun analitiğini görüntüleme yetkiniz yok.");

        var cacheKey = FormCacheKeys.Analytics(formId);
        var cached = await _cache.TryGetAsync<FormAnswerAnalyticsContract>(cacheKey, cancellationToken);
        if (cached != null)
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.Success, Data: cached);

        var form = await _forms.GetByIdAsync(formId, cancellationToken);
        if (form == null)
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var responses = await _metrics.GetNonArchivedResponseDataAsync(formId, cancellationToken);
        var analytics = AnswerAnalyticsBuilder.Build(form, responses);

        await _cache.TrySetAsync(cacheKey, analytics, AnalyticsCacheTtl, cancellationToken);

        return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.Success, Data: analytics);
    }

    public async Task<ServiceResult<FormMetricsContract>> GetFormMetricsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _forms.ExistsAsync(formId, cancellationToken))
            return new ServiceResult<FormMetricsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = await _forms.IsUserCollaboratorAsync(formId, userId, cancellationToken);
        if (!isAuthorized && !await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken))
            return new ServiceResult<FormMetricsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun metriklerini görüntüleme yetkiniz yok.");

        var basicStats = await _metrics.GetFormBasicStatsAsync(formId, cancellationToken);

        var emptyDailyTrend = Enumerable.Range(0, 7).Select(offset =>
        {
            var targetDate = DateTime.UtcNow.AddDays(-6 + offset).Date;
            return new TrendItemContract($"d-{offset}", targetDate.ToString("ddd"), 0);
        }).ToList();

        if (basicStats == null)
        {
            var emptyMetrics = new FormMetricsContract(
                TotalResponses: 0,
                PendingCount: 0,
                ApprovedCount: 0,
                RejectedCount: 0,
                AverageCompletionTime: null,
                DailyTrendPercentage: 0,
                HourlyTrendPercentage: 0,
                SourceBreakdown: new SourceBreakdownContract(0, 0),
                DailyTrend: emptyDailyTrend,
                HourlyTrend: new List<TrendItemContract>()
            );
            return new ServiceResult<FormMetricsContract>(ServiceStatus.Success, Data: emptyMetrics);
        }

        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7).Date;
        var dailyData = await _metrics.GetDailyResponseCountsAsync(formId, sevenDaysAgo, cancellationToken);

        var dailyTrend = Enumerable.Range(0, 7).Select(offset =>
        {
            var targetDate = sevenDaysAgo.AddDays(offset + 1);
            var data = dailyData.FirstOrDefault(d => d.Date == targetDate.Date);

            return new TrendItemContract(
                $"d-{offset}",
                targetDate.ToString("ddd"),
                data?.Count ?? 0
            );
        }).ToList();

        var twentyFourHoursAgo = now.AddHours(-24);
        var hourlyDataRaw = await _metrics.GetHourlyResponseCountsAsync(formId, twentyFourHoursAgo, cancellationToken);

        var hourlyTrend = Enumerable.Range(0, 24).Select(offset =>
        {
            var targetDateTime = twentyFourHoursAgo.AddHours(offset + 1);
            var data = hourlyDataRaw.FirstOrDefault(d => d.Date == targetDateTime.Date && d.Hour == targetDateTime.Hour);

            return new TrendItemContract(
                $"h-{targetDateTime.Hour}",
                targetDateTime.ToString("HH:00"),
                data?.Count ?? 0
            );
        }).ToList();

        var result = new FormMetricsContract(
            basicStats.Total,
            basicStats.Pending,
            basicStats.Approved,
            basicStats.Rejected,
            basicStats.AvgTime,
            CalculateTrendPercentageChange(dailyTrend),
            CalculateTrendPercentageChange(hourlyTrend),
            new SourceBreakdownContract(basicStats.Registered, basicStats.Anonymous),
            dailyTrend,
            hourlyTrend
        );

        return new ServiceResult<FormMetricsContract>(ServiceStatus.Success, Data: result);
    }

    public async Task<ServiceResult<ServiceMetricsContract>> GetServiceMetricsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var totalForms = await _metrics.GetTotalFormsCountAsync(cancellationToken);
        var totalResponses = await _metrics.GetTotalResponsesCountAsync(cancellationToken);
        var pendingResponses = await _metrics.GetPendingNonArchivedResponsesCountAsync(cancellationToken);

        var today = DateTime.UtcNow.Date;
        var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var currentWeekStart = today.AddDays(-1 * diff).Date;

        var weeksToFetch = 8;
        var startDate = currentWeekStart.AddDays(-(weeksToFetch - 1) * 7);

        var formDates = await _metrics.GetFormCreatedDatesAsync(startDate, cancellationToken);
        var responseDates = await _metrics.GetResponseSubmittedDatesAsync(startDate, cancellationToken);

        var formsWeeklyTrend = new List<TrendItemContract>();
        var responsesWeeklyTrend = new List<TrendItemContract>();

        for (int i = weeksToFetch - 1; i >= 0; i--)
        {
            var weekStart = currentWeekStart.AddDays(-i * 7);
            var weekEnd = weekStart.AddDays(7);

            var weekLabel = $"{weekStart:dd MMM}";

            var formCount = formDates.Count(d => d >= weekStart && d < weekEnd);
            var responseCount = responseDates.Count(d => d >= weekStart && d < weekEnd);

            formsWeeklyTrend.Add(new TrendItemContract($"fw-{i}", weekLabel, formCount));
            responsesWeeklyTrend.Add(new TrendItemContract($"rw-{i}", weekLabel, responseCount));
        }

        var result = new ServiceMetricsContract(totalForms, totalResponses, pendingResponses, CalculateTrendPercentageChange(formsWeeklyTrend), CalculateTrendPercentageChange(responsesWeeklyTrend), formsWeeklyTrend, responsesWeeklyTrend);

        return new ServiceResult<ServiceMetricsContract>(ServiceStatus.Success, Data: result);
    }

    private static double CalculateTrendPercentageChange(List<TrendItemContract> trend)
    {
        if (trend == null || trend.Count < 2) return 0;

        var previousItems = trend.Take(trend.Count - 1).Select(t => t.Count).ToList();
        var previousAverage = previousItems.Average();

        var currentCount = trend.Last().Count;

        if (previousAverage == 0)
            return currentCount > 0 ? 100.0 : 0.0;

        var percentageChange = ((currentCount - previousAverage) / previousAverage) * 100;

        return Math.Round(percentageChange, 2);
    }
}
