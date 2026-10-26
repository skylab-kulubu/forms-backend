using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.Attempts;

/// <summary>Adayın yolu: görevi görüntüleme, başlatma ve cevabın süre içinde gelip gelmediği.</summary>
public interface IFormAttemptService
{
    Task<AttemptDisplay> PrepareDisplayAsync(Form form, Guid userId, Guid? stepId, CancellationToken ct = default);
    Task<AttemptDisplay?> FindSettledAsync(Form form, Guid userId, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptStartResult>> StartAsync(Guid formId, Guid userId, CancellationToken ct = default);

    Task<AttemptGate> CheckSubmitAsync(Form form, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Denemeyi gönderilmiş işaretler ama kaydetmez: işaret cevabın kendi commit'ine girer, cevap kaydedilmezse işaret de
    /// yazılmaz. Deneme bu arada değiştiyse commit çakışmayla döner ve cevap da kaydedilmez.
    /// </summary>
    void MarkSubmitted(FormAttempt attempt, Guid responseId);
}

public sealed record AttemptDisplay(FormAttemptDisplayContract Contract, Guid? WorkflowStepId);

public sealed record AttemptGate(FormAttempt? Attempt, ServiceResult<ResponseSubmitResult>? Rejection)
{
    public static AttemptGate Allow(FormAttempt attempt) => new(attempt, null);
    public static AttemptGate Reject(ServiceResult<ResponseSubmitResult> rejection) => new(null, rejection);
}
