using Forms.Domain.Enums;
using Forms.Application.Contracts;
using Forms.Application.Contracts.Metrics;
using Forms.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Forms.Application.Services;

public class FormMetricService : IFormMetricService
{
    private readonly AppDbContext _context;

    public FormMetricService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<FormMetricsContract>> GetFormMetricsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var formExists = await _context.Forms.AnyAsync(f => f.Id == formId, cancellationToken);
        if (!formExists)
            return new ServiceResult<FormMetricsContract>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = await _context.Collaborators.AnyAsync(c => c.FormId == formId && c.UserId == userId && (c.Role != CollaboratorRole.None), cancellationToken);
        if (!isAuthorized)
            return new ServiceResult<FormMetricsContract>(FormAccessStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");

        var query = _context.Responses.AsNoTracking().Where(r => r.FormId == formId);

        var basicStats = await query.GroupBy(r => 1).Select(g => new
        {
            Total = g.Count(),
            Pending = g.Count(r => r.Status == FormResponseStatus.Pending),
            Approved = g.Count(r => r.Status == FormResponseStatus.Approved),
            Rejected = g.Count(r => r.Status == FormResponseStatus.Declined),
            AvgTime = g.Average(r => r.TimeSpent),
            Registered = g.Count(r => r.UserId != null),
            Anonymous = g.Count(r => r.UserId == null)
        }).FirstOrDefaultAsync(cancellationToken);

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
                SourceBreakdown: new SourceBreakdownContract(0, 0),
                DailyTrend: emptyDailyTrend,
                HourlyTrend: new List<TrendItemContract>()
            );
            return new ServiceResult<FormMetricsContract>(FormAccessStatus.Available, Data: emptyMetrics);
        }

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7).Date;
        var dailyData = await query.Where(r => r.SubmittedAt >= sevenDaysAgo)
            .GroupBy(r => r.SubmittedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var dailyTrend = Enumerable.Range(0, 7).Select(offset =>
        {
            var targetDate = DateTime.UtcNow.AddDays(-6 + offset).Date;
            var data = dailyData.FirstOrDefault(d => d.Date == targetDate);
            return new TrendItemContract($"d-{offset}", targetDate.ToString("ddd"), data?.Count ?? 0);
        }).ToList();

        var hourlyData = await query.GroupBy(r => r.SubmittedAt.Hour).Select(g => new { Hour = g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var hourlyTrend = hourlyData.Select(h => new TrendItemContract($"h-{h.Hour}", h.Hour.ToString("00"), h.Count)).ToList();

        var result = new FormMetricsContract(
            basicStats.Total,
            basicStats.Pending,
            basicStats.Approved,
            basicStats.Rejected,
            basicStats.AvgTime,
            new SourceBreakdownContract(basicStats.Registered, basicStats.Anonymous),
            dailyTrend,
            hourlyTrend
        );

        return new ServiceResult<FormMetricsContract>(FormAccessStatus.Available, Data: result);
    }
}