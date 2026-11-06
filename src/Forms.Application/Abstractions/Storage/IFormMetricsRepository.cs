using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormMetricsRepository
{
    Task<IReadOnlyList<ResponseFact>> GetResponseFactsAsync(Guid formId, CancellationToken ct = default);
    Task<IReadOnlyList<List<FormResponseSchemaItem>>> GetNonArchivedResponseDataAsync(Guid formId, CancellationToken ct = default);

    Task<int> GetTotalFormsCountAsync(CancellationToken ct = default);
    Task<int> GetTotalResponsesCountAsync(CancellationToken ct = default);
    Task<int> GetPendingNonArchivedResponsesCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> GetFormCreatedDatesAsync(DateTime sinceDate, CancellationToken ct = default);
    Task<IReadOnlyList<DateTime>> GetResponseSubmittedDatesAsync(DateTime sinceDate, CancellationToken ct = default);
}

public sealed record ResponseFact(
    Guid Id,
    Guid? UserId,
    FormResponseStatus Status,
    DateTime SubmittedAt,
    int? TimeSpent,
    DateTime? ReviewedAt,
    Guid? ReviewedBy,
    string? Source,
    string? Campaign
);
