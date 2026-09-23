using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
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
    /// Bu formların hangi akışta yer aldığı. Yayınlanmış üyelik varsa o bildirilir,
    /// yoksa taslak üyelik. Akışta yer almayan form sözlükte bulunmaz.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, WorkflowFormMembership>> GetFormMembershipsAsync(
        IReadOnlyCollection<Guid> formIds,
        CancellationToken ct = default);

    Task<FormWorkflow?> GetAsync(Guid workflowId, CancellationToken ct = default);
    Task<FormWorkflow?> GetForEditAsync(Guid workflowId, CancellationToken ct = default);

    Task<FormWorkflowVersion?> GetVersionAsync(Guid workflowId, WorkflowStatus status, CancellationToken ct = default);
    Task<FormWorkflowVersion?> GetVersionForEditAsync(Guid workflowId, WorkflowStatus status, CancellationToken ct = default);

    Task<int> GetNextVersionNumberAsync(Guid workflowId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowVersionProjection>> GetVersionsAsync(Guid workflowId, CancellationToken ct = default);
    Task<PagedResult<WorkflowSummaryProjection>> GetOwnedWorkflowsAsync(
        Guid ownerUserId,
        GetWorkflowsRequest request,
        CancellationToken ct = default);

    /// <summary>Kullanıcının Owner olduğu, silinmemiş formlar: adım seçicinin kaynağı.</summary>
    Task<IReadOnlyList<WorkflowCandidateForm>> GetOwnedFormsAsync(Guid ownerUserId, CancellationToken ct = default);

    /// <summary>Graf doğrulamasının ihtiyaç duyduğu form gerçekleri.</summary>
    Task<IReadOnlyDictionary<Guid, WorkflowNodeForm>> GetNodeFormsAsync(
        IReadOnlyCollection<Guid> formIds,
        Guid ownerUserId,
        CancellationToken ct = default);

    /// <summary>Adım kartlarının ihtiyaç duyduğu form başlığı ve formun kendi onay ayarı.</summary>
    Task<IReadOnlyDictionary<Guid, WorkflowFormHeader>> GetFormHeadersAsync(
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

/// <param name="IsPublished">Üyelik yayında mı? Kilitler yalnız yayındayken geçerlidir.</param>
/// <param name="RequiresManualReview">Üyeliğin bildirildiği sürümdeki adımın onay ayarı.</param>
/// <param name="LockedQuestions">Yönlendirme koşullarının dayandığı sorular ve değerler.</param>
public sealed record WorkflowFormMembership(
    Guid WorkflowId,
    string WorkflowName,
    bool IsStart,
    bool IsPublished,
    bool AllowMultipleRuns,
    WorkflowIntake Intake,
    bool RequiresManualReview,
    IReadOnlyCollection<WorkflowLockedQuestion> LockedQuestions);

/// <summary>
/// Bir koşulun okuduğu soru ve karşılaştırdığı metinler. Cevaplar seçeneğin görünen
/// adıyla saklandığı için, o adı değiştirmek koşulu sessizce bozar: değerler de
/// sorunun kendisi kadar korunmalı.
/// </summary>
/// <param name="Values">
/// Yalnız metin karşılaştırmalarından gelir. Sayısal karşılaştırmaların ve
/// boş/dolu kontrollerinin seçenek adıyla ilgisi yoktur, listeye girmezler.
/// </param>
public sealed record WorkflowLockedQuestion(string QuestionId, IReadOnlyCollection<string> Values);

/// <param name="NodeCount">Sürümdeki adım sayısı; legacy 1..5 aşamasını hesaplamak için.</param>
public sealed record WorkflowNodeLocation(
    Guid WorkflowId,
    Guid WorkflowVersionId,
    Guid NodeId,
    string NodeKey,
    bool IsStart,
    bool AllowMultipleRuns,
    WorkflowIntake Intake,
    int NodeCount,
    Guid StartFormId);

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
    WorkflowIntake Intake,
    Guid? StartFormId,
    int NodeCount,
    int? PublishedVersion,
    bool HasUnpublishedChanges,
    DateTime? UpdatedAt);

public sealed record WorkflowCandidateForm(Guid Id, string Title, bool RequiresManualReview);

/// <param name="RequiresManualReview">
/// Formun kendi ayarı; yalnız onay ayarı gönderilmeyen yeni adımın varsayılanıdır.
/// </param>
public sealed record WorkflowFormHeader(string Title, bool RequiresManualReview);
