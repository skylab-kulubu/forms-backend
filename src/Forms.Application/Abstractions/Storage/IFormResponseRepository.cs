using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormResponseRepository
{
    Task<FormResponse?> GetLatestForUserAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<FormResponseCounts> GetCountsAsync(Guid formId, CancellationToken ct = default);
    Task<bool> HasNonArchivedResponseAsync(Guid formId, Guid userId, CancellationToken ct = default);

    Task<FormResponse?> GetByIdWithFormAndCollaboratorsAsync(Guid responseId, CancellationToken ct = default);
    Task<FormResponse?> GetForEditByIdWithFormAndCollaboratorsAsync(Guid responseId, CancellationToken ct = default);

    Task<PagedResponsesProjection> GetPagedAsync(Guid formId, GetResponsesRequest request, bool includeAttempts, DateTime now, CancellationToken ct = default);
    Task<IReadOnlyList<FormResponse>> GetNonArchivedByFormAsync(Guid formId, CancellationToken ct = default);

    Task<IReadOnlyList<OverduePendingFormProjection>> GetOverduePendingByFormAsync(DateTime cutoff, CancellationToken ct = default);
    Task MarkOverduePendingRemindedAsync(DateTime cutoff, DateTime remindedAt, CancellationToken ct = default);

    void Add(FormResponse response);
    void Remove(FormResponse response);
}

public sealed record FormResponseCounts(int Total, int Waiting, double? AverageTimeSpentSeconds);

public sealed record OverduePendingFormProjection(Guid FormId, string FormTitle, int PendingCount, IReadOnlyList<Guid> ReviewerIds);

public sealed record ResponseRowProjection(
    Guid Id,
    Guid? UserId,
    FormResponseStatus? Status,
    bool IsArchived,
    Guid? ReviewedBy,
    Guid? ArchivedBy,
    DateTime? SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt,
    int? TimeSpent,
    ResponseAttemptProjection? Attempt,
    ResponseGuest? Guest = null
);

public sealed record ResponseAttemptProjection(
    Guid Id,
    FormAttemptStatus Status,
    Guid? WorkflowStepId,
    DateTime OpenedAt,
    DateTime? StartedAt,
    DateTime? DeadlineAt,
    DateTime? ExpiredAt,
    DateTime? ReminderSentAt,
    int ExtendedMinutes,
    bool ClosedByTeam,
    DateTime? ResponseSubmittedAt
);

public sealed record ResponseStatusCounts(
    int Submitted,
    int Pending,
    int Approved,
    int Declined,
    int Provisional,
    int Running,
    int Opened,
    int NoSubmission
);

public sealed record PagedResponsesProjection(
    IReadOnlyList<ResponseRowProjection> Items,
    int TotalCount,
    double? AverageTimeSpent,
    ResponseStatusCounts Counts,
    double? AverageTaskSeconds
);
