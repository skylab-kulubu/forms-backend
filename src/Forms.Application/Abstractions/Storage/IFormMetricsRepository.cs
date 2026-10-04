using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormMetricsRepository
{
    Task<FormBasicStats?> GetFormBasicStatsAsync(Guid formId, CancellationToken ct = default);
    Task<IReadOnlyList<List<FormResponseSchemaItem>>> GetNonArchivedResponseDataAsync(Guid formId, CancellationToken ct = default);
    Task<IReadOnlyList<DailyResponseCount>> GetDailyResponseCountsAsync(Guid formId, DateTime sinceDate, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyResponseCount>> GetHourlyResponseCountsAsync(Guid formId, DateTime sinceTime, CancellationToken ct = default);
    Task<IReadOnlyList<ResponseSourceCount>> GetResponseSourceCountsAsync(Guid formId, DateTime sinceTime, CancellationToken ct = default);

    Task<int> GetTotalFormsCountAsync(CancellationToken ct = default);
    Task<int> GetTotalResponsesCountAsync(CancellationToken ct = default);
    Task<int> GetPendingNonArchivedResponsesCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> GetFormCreatedDatesAsync(DateTime sinceDate, CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> GetResponseSubmittedDatesAsync(DateTime sinceDate, CancellationToken ct = default);
}

public sealed record FormBasicStats(
    int Total,
    int Pending,
    int Approved,
    int Rejected,
    double? AvgTime,
    int Registered,
    int Anonymous
);

public sealed record DailyResponseCount(DateTime Date, int Count);

public sealed record HourlyResponseCount(DateTime Date, int Hour, int Count);

public sealed record ResponseSourceCount(string? Source, int Count);
