using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.Attempts;

public interface IFormAttemptService
{
    Task<AttemptDisplay> PrepareDisplayAsync(Form form, Guid userId, Guid? stepId, CancellationToken ct = default);
    Task<AttemptDisplay?> FindSettledAsync(Form form, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptStartResult>> StartAsync(Guid formId, Guid userId, CancellationToken ct = default);

    Task<AttemptGate> CheckSubmitAsync(Form form, Guid userId, CancellationToken ct = default);
    Task MarkSubmittedAsync(Guid attemptId, Guid responseId, CancellationToken ct = default);

    Task<ServiceResult<FormAttemptDetailContract>> ExtendAsync(Guid attemptId, Guid actorId, AttemptExtendRequest request, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptDetailContract>> AcceptAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptDetailContract>> CloseAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptDetailContract>> RemindAsync(Guid attemptId, Guid actorId, CancellationToken ct = default);

    Task<ServiceResult<FormAttemptViewContract>> GetViewAsync(Guid attemptId, Guid userId, CancellationToken ct = default);
    Task<FormAttemptDetailContract?> GetDetailForResponseAsync(FormResponse response, CancellationToken ct = default);
    Task<Guid?> FindStepIdForResponseAsync(Guid responseId, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptAnalyticsContract>> GetAnalyticsAsync(Guid formId, Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int take, CancellationToken ct = default);
    Task ExpireByIdAsync(Guid attemptId, CancellationToken ct = default);
}

public sealed record AttemptDisplay(FormAttemptDisplayContract Contract, Guid? WorkflowStepId);

public sealed record AttemptGate(FormAttempt? Attempt, ServiceResult<ResponseSubmitResult>? Rejection)
{
    public static AttemptGate Allow(FormAttempt attempt) => new(attempt, null);
    public static AttemptGate Reject(ServiceResult<ResponseSubmitResult> rejection) => new(null, rejection);
}
