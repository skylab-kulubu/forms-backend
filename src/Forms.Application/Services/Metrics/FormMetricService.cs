using System.Globalization;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Metrics;
using Skylab.Forms.Application.Services.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services;

public class FormMetricService : IFormMetricService
{
    // Short safety TTL only: explicit invalidation (submit/archive/schema change) handles the
    // common case. Keeping this small also bounds staleness from a rare read/invalidate race
    // (a request that reads the DB just before a concurrent write repopulates the old snapshot).
    private static readonly TimeSpan AnalyticsCacheTtl = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan LinkStatsCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RespondentsCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PeopleLookupLimit = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    private readonly IFormRepository _forms;
    private readonly IFormMetricsRepository _metrics;
    private readonly IFormWorkflowRepository _workflows;
    private readonly IUserDirectory _users;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheService _cache;
    private readonly IShortLinkProvider _links;
    private readonly TimeProvider _time;

    public FormMetricService(
        IFormRepository forms,
        IFormMetricsRepository metrics,
        IFormWorkflowRepository workflows,
        IUserDirectory users,
        ICurrentUser currentUser,
        ICacheService cache,
        IShortLinkProvider links,
        TimeProvider time)
    {
        _forms = forms;
        _metrics = metrics;
        _workflows = workflows;
        _users = users;
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

    public async Task<ServiceResult<PagedResult<QuestionAnswerContract>>> GetQuestionAnswersAsync(Guid formId, string questionId, Guid userId, GetQuestionAnswersRequest request, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(formId, cancellationToken);
        if (form == null)
            return new ServiceResult<PagedResult<QuestionAnswerContract>>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!FormAccess.Allows(await _forms.GetRoleAsync(formId, userId, cancellationToken), _currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<PagedResult<QuestionAnswerContract>>(ServiceStatus.NotAuthorized, Message: "Bu formun cevaplarını görüntüleme yetkiniz yok.");

        var responses = await GetFilteredAnswersAsync(form, request.Filter, _time.GetUtcNow().UtcDateTime, cancellationToken);

        var answers = responses
            .Select(response => (Response: response, Answer: response.Data.FirstOrDefault(item => item.Id == questionId)?.Answer))
            .Where(row => !string.IsNullOrWhiteSpace(row.Answer))
            .OrderByDescending(row => row.Response.SubmittedAt)
            .ToList();

        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lookup.CancelAfter(PeopleLookupLimit);

        var search = request.Search?.Trim() ?? string.Empty;
        var personQuestions = new FormPersonQuestions(form.Schema);

        bool FoundInForms(ResponseAnswers response, string? answer) =>
            Holds(answer, search) || HoldsPerson(response, personQuestions, search);

        var needsNames = search.Length > 0 && answers.Any(row =>
            row.Response.UserId != null
            && !personQuestions.NameIn(row.Response.Data)
            && !FoundInForms(row.Response, row.Answer));

        var people = needsNames
            ? await GetRespondentsAsync(formId, lookup, cancellationToken)
            : new Dictionary<Guid, UserContract>();

        var matching = search.Length == 0
            ? answers
            : answers
                .Where(row => FoundInForms(row.Response, row.Answer)
                    || HoldsUser(people.GetValueOrDefault(row.Response.UserId ?? Guid.Empty), search))
                .ToList();

        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var slice = matching.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var unknown = slice
            .Where(row => row.Response.UserId is { } id && !people.ContainsKey(id))
            .Select(row => row.Response.UserId!.Value);

        foreach (var user in await GetUsersInTimeAsync(unknown, lookup, cancellationToken))
            people[user.Id] = user;

        var items = slice
            .Select(row => new QuestionAnswerContract(
                row.Response.Id,
                row.Response.UserId is { } id ? people.GetValueOrDefault(id) : null,
                row.Response.Guest,
                row.Response.SubmittedAt,
                row.Response.Status,
                FormAnswerText.ToDisplayText(row.Answer).Trim()))
            .ToList();

        return new ServiceResult<PagedResult<QuestionAnswerContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<QuestionAnswerContract>(items, matching.Count, page, pageSize));
    }

    private async Task<Dictionary<Guid, UserContract>> GetRespondentsAsync(Guid formId, CancellationTokenSource lookup, CancellationToken cancellationToken)
    {
        var cacheKey = FormCacheKeys.Respondents(formId);

        var users = await _cache.TryGetAsync<List<UserContract>>(cacheKey, cancellationToken);

        if (users is null)
        {
            var responses = await _metrics.GetResponseFactsAsync(formId, cancellationToken);
            var ids = responses.Where(response => response.UserId != null).Select(response => response.UserId!.Value).Distinct().ToList();

            users = await GetUsersInTimeAsync(ids, lookup, cancellationToken);

            if (users.Count == ids.Count)
                await _cache.TrySetAsync(cacheKey, users, RespondentsCacheTtl, cancellationToken);
        }

        return users.DistinctBy(user => user.Id).ToDictionary(user => user.Id);
    }

    private async Task<List<UserContract>> GetUsersInTimeAsync(IEnumerable<Guid> ids, CancellationTokenSource lookup, CancellationToken cancellationToken)
    {
        try
        {
            return await _users.GetUsersAsync(ids, lookup.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    private static bool Holds(string? text, string search) =>
        text is not null && ClubTime.Culture.CompareInfo.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0;

    private static bool HoldsPerson(ResponseAnswers response, FormPersonQuestions questions, string search) =>
        Holds(questions.TextIn(response.Data), search)
        || Holds($"{response.Guest?.FirstName} {response.Guest?.LastName}", search)
        || Holds(response.Guest?.Email, search);

    private static bool HoldsUser(UserContract? user, string search) =>
        Holds(user?.FullName, search) || Holds(user?.Email, search);

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
