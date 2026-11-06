using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Attribution;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Metrics;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services;

public static class FormMetricsBuilder
{
    // Core tıklamaları 90 gün saklıyor; yanıtlar da aynı pencereden sayılır ki dönüşüm tutarlı olsun.
    private const int ChannelWindowDays = 90;

    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    public static FormMetricsContract Build(
        IReadOnlyCollection<ResponseFact> responses,
        ShortLinkStats? linkStats,
        DateTime? closesAt,
        DateTime now)
    {
        var times = responses.Select(response => response.SubmittedAt).ToList();
        var local = times.Select(ClubTime.ToLocal).ToList();
        var localNow = ClubTime.ToLocal(now);
        var spent = responses.Where(response => response.TimeSpent > 0).Select(response => (double)response.TimeSpent!.Value).ToList();
        var flagged = responses.Count(response => response.Status == FormResponseStatus.Flagged);

        return new FormMetricsContract(
            responses.Count,
            responses.Count(response => response.Status == FormResponseStatus.Pending) + flagged,
            responses.Count(response => response.Status == FormResponseStatus.Approved),
            responses.Count(response => response.Status == FormResponseStatus.Declined),
            spent.Count > 0 ? spent.Average() : null,
            WindowChange(times, now, Week),
            WindowChange(times, now, Day),
            new SourceBreakdownContract(responses.Count(response => response.UserId != null), responses.Count(response => response.UserId == null)),
            DailyTrend(localNow, local),
            HourlyTrend(localNow, local),
            ChannelsOf(responses, linkStats, now),
            Statistics.Median(spent),
            flagged,
            ReviewOf(responses),
            LifetimeOf(local, closesAt, localNow));
    }

    public static double WindowChange(IReadOnlyCollection<DateTime> times, DateTime now, TimeSpan window)
    {
        var current = times.Count(at => at > now - window && at <= now);
        var previous = times.Count(at => at > now - 2 * window && at <= now - window);

        if (previous == 0) return current > 0 ? 100.0 : 0.0;

        return Math.Round((current - previous) / (double)previous * 100, 2);
    }

    private static List<TrendItemContract> DailyTrend(DateTime localNow, IReadOnlyCollection<DateTime> local)
    {
        var firstDay = localNow.Date.AddDays(-6);

        return Enumerable.Range(0, 7).Select(offset =>
        {
            var day = firstDay.AddDays(offset);
            return new TrendItemContract($"d-{offset}", day.ToString("ddd", ClubTime.Culture), local.Count(at => at.Date == day));
        }).ToList();
    }

    private static List<TrendItemContract> HourlyTrend(DateTime localNow, IReadOnlyCollection<DateTime> local)
    {
        var firstHour = localNow.Date.AddHours(localNow.Hour - 23);

        return Enumerable.Range(0, 24).Select(offset =>
        {
            var hour = firstHour.AddHours(offset);

            return new TrendItemContract(
                $"h-{hour.Hour}",
                hour.ToString("HH:00", ClubTime.Culture),
                local.Count(at => at >= hour && at < hour.AddHours(1)));
        }).ToList();
    }

    /// <summary>
    /// Kanal başına yanıt (forms) ve tıklama (core) sayısı. Etiketsiz satırın kaynağı null ve
    /// sonda durur; core'a ulaşılamazsa tıklamalar null döner, yanıtlar yine gelir.
    /// </summary>
    private static List<ChannelMetricContract> ChannelsOf(IReadOnlyCollection<ResponseFact> responses, ShortLinkStats? linkStats, DateTime now)
    {
        var since = now.AddDays(-ChannelWindowDays);

        var bySource = responses
            .Where(response => response.SubmittedAt >= since)
            .GroupBy(response => SourceKey(response.Source), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var links = (linkStats?.Sources ?? [])
            .GroupBy(item => SourceKey(item.Source), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (Clicks: group.Sum(item => item.Count), Scans: group.Sum(item => item.Scans)), StringComparer.Ordinal);

        return bySource.Keys
            .Union(links.Keys, StringComparer.Ordinal)
            .Select(key =>
            {
                var rows = bySource.GetValueOrDefault(key) ?? [];
                var link = links.GetValueOrDefault(key);
                var (approved, declined) = DecisionsIn(rows);

                return new ChannelMetricContract(
                    key.Length == 0 ? null : key,
                    rows.Count,
                    linkStats is null ? null : link.Clicks,
                    linkStats is null ? null : link.Scans,
                    approved,
                    declined,
                    key.Length == 0 ? null : CampaignsOf(rows));
            })
            .OrderBy(channel => channel.Source is null)
            .ThenByDescending(channel => channel.Responses)
            .ThenByDescending(channel => channel.Clicks ?? 0)
            .ThenBy(channel => channel.Source, StringComparer.Ordinal)
            .ToList();
    }

    private static string SourceKey(string? source) => AttributionNormalizer.NormalizeSource(source) ?? string.Empty;

    private static List<ChannelCampaignContract> CampaignsOf(IReadOnlyCollection<ResponseFact> rows) =>
        rows
            .GroupBy(response => response.Campaign, StringComparer.Ordinal)
            .Select(group =>
            {
                var (approved, declined) = DecisionsIn(group);
                return new ChannelCampaignContract(group.Key, group.Count(), approved, declined);
            })
            .OrderBy(campaign => campaign.Campaign is null)
            .ThenByDescending(campaign => campaign.Responses)
            .ThenBy(campaign => campaign.Campaign, StringComparer.Ordinal)
            .ToList();

    private static (int Approved, int Declined) DecisionsIn(IEnumerable<ResponseFact> rows)
    {
        var approved = 0;
        var declined = 0;

        foreach (var row in rows)
        {
            if (row.Status == FormResponseStatus.Approved) approved++;
            if (row.Status == FormResponseStatus.Declined) declined++;
        }

        return (approved, declined);
    }

    private static IEnumerable<ResponseFact> Decided(IEnumerable<ResponseFact> responses) =>
        responses.Where(response => response.Status is FormResponseStatus.Approved or FormResponseStatus.Declined
            && response.ReviewedAt != null
            && response.ReviewedBy != null);

    private static ResponseFact? OldestPending(IEnumerable<ResponseFact> responses) =>
        responses.Where(response => response.Status == FormResponseStatus.Pending).MinBy(response => response.SubmittedAt);

    private static FormReviewMetricsContract? ReviewOf(IReadOnlyCollection<ResponseFact> responses)
    {
        var decided = Decided(responses).ToList();
        var oldest = OldestPending(responses);

        if (decided.Count == 0 && oldest is null) return null;

        var reviewers = decided
            .GroupBy(response => response.ReviewedBy!.Value)
            .Select(group => new FormReviewerContract(group.Key, group.Count()))
            .OrderByDescending(reviewer => reviewer.Decisions)
            .ToList();

        var waited = Statistics.Median(decided.Select(response => Math.Max(0, (response.ReviewedAt!.Value - response.SubmittedAt).TotalSeconds)));

        return new FormReviewMetricsContract(
            decided.Count,
            waited is { } seconds ? Math.Round(seconds) : null,
            oldest is null ? null : new OldestPendingResponseContract(oldest.Id, oldest.SubmittedAt),
            reviewers);
    }

    private static FormLifetimeContract? LifetimeOf(IReadOnlyCollection<DateTime> local, DateTime? closesAt, DateTime localNow)
    {
        if (local.Count == 0) return null;

        var counts = local
            .GroupBy(DateOnly.FromDateTime)
            .ToDictionary(group => group.Key, group => group.Count());

        var first = counts.Keys.Min();
        var last = DateOnly.FromDateTime(localNow);

        var days = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1)
            .Select(first.AddDays)
            .Select(day => new FormLifetimeDayContract(day, counts.GetValueOrDefault(day)))
            .ToList();

        return new FormLifetimeContract(closesAt, days);
    }
}
