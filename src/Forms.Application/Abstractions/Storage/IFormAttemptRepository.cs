using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormAttemptRepository
{
    Task<FormAttempt?> GetLatestForEditAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<FormAttempt?> GetLatestAsync(Guid formId, Guid userId, CancellationToken ct = default);
    Task<FormAttempt?> GetForEditAsync(Guid attemptId, CancellationToken ct = default);
    Task<FormAttempt?> GetWithFormAsync(Guid attemptId, CancellationToken ct = default);
    Task<FormAttempt?> GetByResponseAsync(Guid responseId, CancellationToken ct = default);
    Task<IReadOnlyList<FormAttemptEvent>> GetEventsAsync(Guid attemptId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime deadlineBefore, int take, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, FormAttemptTiming>> GetTimingsByResponseAsync(Guid formId, CancellationToken ct = default);
    Task<FormAttemptStatsProjection> GetStatsAsync(Guid formId, CancellationToken ct = default);

    void Add(FormAttempt attempt);
    void Add(FormAttemptEvent attemptEvent);
}

public sealed record FormAttemptTiming(DateTime? StartedAt, DateTime? DeadlineAt, int ExtendedMinutes);

public sealed record FormAttemptDuration(DateTime StartedAt, DateTime SubmittedAt);

public sealed record FormAttemptExtensionFact(Guid AttemptId, Guid? ActorUserId, int Minutes);

public sealed record FormAttemptStatsProjection(
    int Opened,
    int Started,
    int Submitted,
    int Provisional,
    int Running,
    int NoSubmission,
    int ExpiredWithDraft,
    IReadOnlyList<FormAttemptDuration> Durations,
    IReadOnlyList<FormAttemptExtensionFact> Extensions,
    IReadOnlyList<List<FormResponseSchemaItem>> ExpiredDrafts);
