using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.Metrics;
using Skylab.Forms.Application.Services.Forms;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services;

public class FormMetricService : IFormMetricService
{
    // Short safety TTL only: explicit invalidation (submit/archive/schema change) handles the
    // common case. Keeping this small also bounds staleness from a rare read/invalidate race
    // (a request that reads the DB just before a concurrent write repopulates the old snapshot).
    private static readonly TimeSpan AnalyticsCacheTtl = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan LinkStatsCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly IFormRepository _forms;
    private readonly IFormMetricsRepository _metrics;
    private readonly IFormWorkflowRepository _workflows;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheService _cache;
    private readonly IShortLinkProvider _links;
    private readonly TimeProvider _time;

    public FormMetricService(
        IFormRepository forms,
        IFormMetricsRepository metrics,
        IFormWorkflowRepository workflows,
        ICurrentUser currentUser,
        ICacheService cache,
        IShortLinkProvider links,
        TimeProvider time)
    {
        _forms = forms;
        _metrics = metrics;
        _workflows = workflows;
        _currentUser = currentUser;
        _cache = cache;
        _links = links;
        _time = time;
    }

    public async Task<ServiceResult<FormAnswerAnalyticsContract>> GetAnswerAnalyticsAsync(Guid formId, Guid userId, GetFormAnalyticsRequest request, CancellationToken cancellationToken = default)
    {
        if (!await _forms.ExistsAsync(formId, cancellationToken))
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!FormAccess.Allows(await _forms.GetRoleAsync(formId, userId, cancellationToken), _currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun analitiğini görüntüleme yetkiniz yok.");

        var cacheKey = FormCacheKeys.Analytics(formId);
        if (!request.IsFiltered && await _cache.TryGetAsync<FormAnswerAnalyticsContract>(cacheKey, cancellationToken) is { } cached)
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.Success, Data: cached);

        var form = await _forms.GetByIdAsync(formId, cancellationToken);
        if (form == null)
            return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var now = _time.GetUtcNow().UtcDateTime;
        var responses = await GetFilteredAnswersAsync(form, request, now, cancellationToken);
        var analytics = AnswerAnalyticsBuilder.Build(form, responses, now);

        if (!request.IsFiltered)
            await _cache.TrySetAsync(cacheKey, analytics, AnalyticsCacheTtl, cancellationToken);

        return new ServiceResult<FormAnswerAnalyticsContract>(ServiceStatus.Success, Data: analytics);
    }

    private async Task<IReadOnlyList<ResponseAnswers>> GetFilteredAnswersAsync(Form form, GetFormAnalyticsRequest filter, DateTime now, CancellationToken cancellationToken)
    {
        var since = filter.SinceDays is > 0 and var days ? now.AddDays(-days) : (DateTime?)null;
        var responses = await _metrics.GetResponseAnswersAsync(form.Id, filter.Status, since, cancellationToken);

        if (string.IsNullOrEmpty(filter.AnswerQuestionId)) return responses;

        var gaveAnswer = AnswerAnalyticsBuilder.GaveAnswer(form, filter.AnswerQuestionId, filter.AnswerValue);

        return [.. responses.Where(response => gaveAnswer(response.Data))];
    }

    public async Task<ServiceResult<FormMetricsContract>> GetFormMetricsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(formId, cancellationToken);
        if (form == null)
            return new ServiceResult<FormMetricsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!FormAccess.Allows(await _forms.GetRoleAsync(formId, userId, cancellationToken), _currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<FormMetricsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun metriklerini görüntüleme yetkiniz yok.");

        var responses = await _metrics.GetResponseFactsAsync(formId, cancellationToken);
        var links = await GetLinkStatsAsync(formId, cancellationToken);
        var membership = await FormEditorMapping.MembershipOfAsync(_workflows, formId, cancellationToken);

        var closesAt = membership is { IsPublished: true, IsStart: true, IntakeClosesAt: { } intakeClosesAt } ? intakeClosesAt : form.ClosesAt;

        return new ServiceResult<FormMetricsContract>(
            ServiceStatus.Success,
            Data: FormMetricsBuilder.Build(responses, links, closesAt, _time.GetUtcNow().UtcDateTime));
    }

    private async Task<ShortLinkStats?> GetLinkStatsAsync(Guid formId, CancellationToken cancellationToken)
    {
        var cacheKey = FormCacheKeys.LinkStats(formId);
        var cached = await _cache.TryGetAsync<ShortLinkStats>(cacheKey, cancellationToken);
        if (cached != null) return cached;

        var stats = await _links.GetStatsAsync(formId, cancellationToken);
        if (stats != null) await _cache.TrySetAsync(cacheKey, stats, LinkStatsCacheTtl, cancellationToken);
        return stats;
    }

    public async Task<ServiceResult<ServiceMetricsContract>> GetServiceMetricsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var totalForms = await _metrics.GetTotalFormsCountAsync(cancellationToken);
        var totalResponses = await _metrics.GetTotalResponsesCountAsync(cancellationToken);
        var pendingResponses = await _metrics.GetPendingNonArchivedResponsesCountAsync(cancellationToken);

        var now = ClubTime.ToLocal(_time.GetUtcNow().UtcDateTime);
        var today = now.Date;
        var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
        var currentWeekStart = today.AddDays(-1 * diff).Date;

        var weeksToFetch = 8;
        var startDate = currentWeekStart.AddDays(-(weeksToFetch - 1) * 7);

        var formDates = (await _metrics.GetFormCreatedDatesAsync(ClubTime.ToUtc(startDate), cancellationToken)).Select(ClubTime.ToLocal).ToList();
        var responseDates = (await _metrics.GetResponseSubmittedDatesAsync(ClubTime.ToUtc(startDate), cancellationToken)).Select(ClubTime.ToLocal).ToList();

        var formsWeeklyTrend = new List<TrendItemContract>();
        var responsesWeeklyTrend = new List<TrendItemContract>();

        for (int i = weeksToFetch - 1; i >= 0; i--)
        {
            var weekStart = currentWeekStart.AddDays(-i * 7);
            var weekEnd = weekStart.AddDays(7);

            var weekLabel = weekStart.ToString("dd MMM", ClubTime.Culture);

            var formCount = formDates.Count(d => d >= weekStart && d < weekEnd);
            var responseCount = responseDates.Count(d => d >= weekStart && d < weekEnd);

            formsWeeklyTrend.Add(new TrendItemContract($"fw-{i}", weekLabel, formCount));
            responsesWeeklyTrend.Add(new TrendItemContract($"rw-{i}", weekLabel, responseCount));
        }

        var result = new ServiceMetricsContract(
            totalForms,
            totalResponses,
            pendingResponses,
            FormMetricsBuilder.WindowChange(formDates, now, Week),
            FormMetricsBuilder.WindowChange(responseDates, now, Week),
            formsWeeklyTrend,
            responsesWeeklyTrend);

        return new ServiceResult<ServiceMetricsContract>(ServiceStatus.Success, Data: result);
    }
}
