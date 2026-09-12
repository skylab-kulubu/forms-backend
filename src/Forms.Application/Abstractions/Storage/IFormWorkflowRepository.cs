using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormWorkflowRepository
{
    /// <summary>
    /// Formun yayındaki bir akışta hangi adım olduğunu bulur. Yeni başvuru
    /// başlatmak için kullanılır; devam eden başvurular kendi version'ından okunur.
    /// </summary>
    Task<WorkflowNodeLocation?> FindPublishedNodeAsync(Guid formId, CancellationToken ct = default);

    Task<WorkflowDefinition?> GetDefinitionAsync(Guid workflowVersionId, CancellationToken ct = default);

    /// <summary>
    /// Form yayındaki bir akışta kullanılıyorsa, onu kısıtlayan bilgiler. null ise
    /// form serbestçe düzenlenebilir.
    /// </summary>
    Task<WorkflowFormLock?> GetPublishedLockAsync(Guid formId, CancellationToken ct = default);
}

/// <param name="LockedQuestionIds">Yönlendirme koşullarının dayandığı, silinemez sorular.</param>
public sealed record WorkflowFormLock(string WorkflowName, IReadOnlyCollection<string> LockedQuestionIds);

public sealed record WorkflowNodeLocation(
    Guid WorkflowId,
    Guid WorkflowVersionId,
    Guid NodeId,
    string NodeKey,
    bool IsStart,
    bool AllowMultipleRuns);

public sealed record WorkflowDefinition(
    Guid WorkflowId,
    Guid WorkflowVersionId,
    IReadOnlyList<FormWorkflowNode> Nodes,
    IReadOnlyList<FormWorkflowTransition> Transitions);
