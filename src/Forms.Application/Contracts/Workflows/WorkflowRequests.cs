using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Workflows;

public record WorkflowUpsertRequest(
    string Name,
    string? Description,
    bool AllowMultipleRuns
);

/// <summary>
/// Taslak tanımın tamamı. Editör parça güncelleme yapamaz: yarım bir graf hiçbir
/// zaman kaydedilmesin diye node ve yönlendirme listeleri birlikte gelir.
/// </summary>
public record WorkflowDefinitionRequest(
    List<WorkflowNodeRequest> Nodes,
    List<WorkflowTransitionRequest> Transitions
);

/// <param name="Position">İsteğe bağlı; tuval çizmeyen bir istemci göndermeyebilir.</param>
public record WorkflowNodeRequest(
    string NodeKey,
    Guid FormId,
    bool IsStart,
    WorkflowNodePositionContract? Position = null
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
