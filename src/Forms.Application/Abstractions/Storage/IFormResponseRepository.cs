using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormResponseRepository
{
    Task<FormResponse?> GetLatestForUserAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<FormResponseCounts> GetCountsAsync(Guid formId, CancellationToken ct = default);
    Task<bool> HasNonArchivedResponseAsync(Guid formId, Guid userId, CancellationToken ct = default);

    Task<FormResponse?> GetByIdWithFormAndCollaboratorsAsync(Guid responseId, CancellationToken ct = default);
    Task<FormResponse?> GetForEditByIdWithFormAndCollaboratorsAsync(Guid responseId, CancellationToken ct = default);

    Task<PagedResponsesProjection> GetPagedAsync(Guid formId, GetResponsesRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<FormResponse>> GetNonArchivedByFormAsync(Guid formId, CancellationToken ct = default);

    Task<IReadOnlyList<OverduePendingFormProjection>> GetOverduePendingByFormAsync(DateTime cutoff, CancellationToken ct = default);
    Task MarkOverduePendingRemindedAsync(DateTime cutoff, DateTime remindedAt, CancellationToken ct = default);

    void Add(FormResponse response);
}

public sealed record FormResponseCounts(int Total, int Waiting, double? AverageTimeSpentSeconds);

public sealed record OverduePendingFormProjection(Guid FormId, string FormTitle, int PendingCount, IReadOnlyList<Guid> ReviewerIds);

public sealed record ResponseSummaryProjection(
    Guid Id,
    Guid? UserId,
    FormResponseStatus Status,
    bool IsArchived,
    Guid? ReviewedBy,
    Guid? ArchivedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt
);

public sealed record PagedResponsesProjection(
    IReadOnlyList<ResponseSummaryProjection> Items,
    int TotalCount,
    double? AverageTimeSpent
);
