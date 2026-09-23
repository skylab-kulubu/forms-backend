using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Workflows;

public record WorkflowUpsertRequest(
    string Name,
    string? Description,
    bool AllowMultipleRuns
);

/// <summary>Liste sorgusu; sıralama her zaman son güncellemeye göredir.</summary>
public record GetWorkflowsRequest(
    int Page = 1,
    int PageSize = 10,
    string? Search = null,
    string SortDirection = "descending"
);

/// <param name="Intake">Boş gelen istek reddedilir; eksik bir alan akışı sessizce yeniden açmasın.</param>
public record WorkflowIntakeRequest(WorkflowIntake? Intake);

/// <summary>
/// Taslak tanımın tamamı. Editör parça güncelleme yapamaz: yarım bir graf hiçbir
/// zaman kaydedilmesin diye node ve yönlendirme listeleri birlikte gelir.
/// </summary>
public record WorkflowDefinitionRequest(
    List<WorkflowNodeRequest> Nodes,
    List<WorkflowTransitionRequest> Transitions
);

/// <param name="Position">İsteğe bağlı; tuval çizmeyen bir istemci göndermeyebilir.</param>
/// <param name="RequiresManualReview">Boş gelirse adım, formun kendi onay ayarıyla kaydedilir.</param>
public record WorkflowNodeRequest(
    string NodeKey,
    Guid FormId,
    bool IsStart,
    WorkflowNodePositionContract? Position = null,
    bool? RequiresManualReview = null
);

/// <summary>
/// Adımlara Id yerine NodeKey ile referans verilir; Id'ler her version'da yeniden
/// üretildiği için editörün elindeki tanım version'dan bağımsız kalır.
/// </summary>
/// <param name="TargetNodeKey">Boş ise akış bu yönlendirmede tamamlanır.</param>
/// <param name="Condition">Boş ise bu, grubun varsayılan rotasıdır.</param>
public record WorkflowTransitionRequest(
    string SourceNodeKey,
    string? TargetNodeKey,
    WorkflowTransitionTrigger Trigger,
    WorkflowConditionGroup? Condition,
    int Priority
);
