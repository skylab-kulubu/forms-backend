using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Workflows;

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

    Task<FormWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default);
    Task<FormWorkflow?> GetForEditAsync(Guid workflowId, CancellationToken ct = default);

    Task<FormWorkflowVersion?> GetVersionAsync(Guid workflowId, WorkflowStatus status, CancellationToken ct = default);
    Task<FormWorkflowVersion?> GetVersionForEditAsync(Guid workflowId, WorkflowStatus status, CancellationToken ct = default);

    Task<int> GetNextVersionNumberAsync(Guid workflowId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowVersionProjection>> GetVersionsAsync(Guid workflowId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowSummaryProjection>> GetOwnedWorkflowsAsync(Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Graf doğrulamasının ihtiyaç duyduğu form gerçekleri.</summary>
    Task<IReadOnlyDictionary<Guid, WorkflowNodeForm>> GetNodeFormsAsync(
        IReadOnlyCollection<Guid> formIds,
        Guid ownerUserId,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> GetFormTitlesAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default);

    /// <summary>
    /// Bu formlardan hangileri başka bir yayınlanmış akışta kullanılıyor? Bir form
    /// iki yayında birden yer alırsa, cevabın hangi akışa ait olduğu belirsizleşir.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> FindFormsInOtherPublishedWorkflowsAsync(
        Guid workflowId,
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default);

    /// <summary>
    /// Legacy bağlı form akışının parçası olan formlar. İki motorun aynı forma
    /// yönlendirme yapmasını engellemek için yayın öncesi kontrol edilir.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> FindLegacyLinkedFormsAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default);

    void Add(FormWorkflow workflow);
    void Add(FormWorkflowVersion version);
    void AddRange(IEnumerable<FormWorkflowNode> nodes);
    void AddRange(IEnumerable<FormWorkflowTransition> transitions);
    void RemoveRange(IEnumerable<FormWorkflowNode> nodes);
    void RemoveRange(IEnumerable<FormWorkflowTransition> transitions);
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

public sealed record WorkflowVersionProjection(
    Guid Id,
    int Version,
    WorkflowStatus Status,
    DateTime? PublishedAt,
    int NodeCount);

public sealed record WorkflowSummaryProjection(
    Guid Id,
    string Name,
    WorkflowStatus Status,
    bool AllowMultipleRuns,
    int PublishedNodeCount,
    DateTime? UpdatedAt);
