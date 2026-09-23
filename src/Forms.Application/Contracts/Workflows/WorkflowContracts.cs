using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Workflows;

/// <param name="Draft">Düzenlenebilir sürüm; yoksa editör yayındakinden başlar.</param>
public record WorkflowContract(
    Guid Id,
    string Name,
    string? Description,
    WorkflowStatus Status,
    bool AllowMultipleRuns,
    UserContract Owner,
    WorkflowVersionContract? Draft,
    WorkflowVersionContract? Published,
    /// <summary>Taslağın son doğrulama sonucu; ayrıca /validate çağırmaya gerek yok.</summary>
    WorkflowValidationContract Validation,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record WorkflowVersionContract(
    Guid Id,
    int Version,
    WorkflowStatus Status,
    DateTime? PublishedAt,
    List<WorkflowNodeContract> Nodes,
    List<WorkflowTransitionContract> Transitions
);

/// <param name="RequiresManualReview">
/// Adım onay gerektiriyor mu? Formun kendi ayarından bağımsızdır; editörün hangi
/// tetikleri sunacağını bu belirler.
/// </param>
/// <param name="Position">Tuvaldeki yer; eski tanımlarda null gelir, istemci kendi yerleştirir.</param>
public record WorkflowNodeContract(
    string NodeKey,
    Guid FormId,
    string FormTitle,
    bool RequiresManualReview,
    bool IsStart,
    WorkflowNodePositionContract? Position
);

/// <summary>İstemci piksel biriminde tam sayı koordinat; sunucu dönüştürmez, doğrulamaz.</summary>
public record WorkflowNodePositionContract(int X, int Y);

public record WorkflowTransitionContract(
    string SourceNodeKey,
    string? TargetNodeKey,
    WorkflowTransitionTrigger Trigger,
    WorkflowConditionGroup? Condition,
    int Priority
);

/// <param name="PublishedVersion">Yayındaki sürüm numarası; hiç yayınlanmadıysa null.</param>
/// <param name="HasUnpublishedChanges">Yayınlanmamış bir taslak var mı?</param>
public record WorkflowSummaryContract(
    Guid Id,
    string Name,
    WorkflowStatus Status,
    bool AllowMultipleRuns,
    WorkflowFormRefContract? StartForm,
    int NodeCount,
    int? PublishedVersion,
    bool HasUnpublishedChanges,
    DateTime? UpdatedAt
);

public record WorkflowFormRefContract(
    Guid Id,
    string Title
);

/// <param name="Reason">
/// Uygun değilse sebebin sabit kodu: formClosed, formAnonymous, formNotOwned,
/// formInAnotherWorkflow, formIsLegacyLinked. Doğrulama kodlarıyla aynı sözlük.
/// </param>
/// <param name="RequiresManualReview">
/// Formun kendi ayarı. Editör bunu yeni eklenen adımın varsayılanı olarak kullanır;
/// adım eklendikten sonra kendi ayarını taşır.
/// </param>
public record WorkflowAvailableFormContract(
    Guid Id,
    string Title,
    bool RequiresManualReview,
    bool IsEligible,
    string? Reason,
    bool IsUsedInThisWorkflow
);

public record WorkflowVersionSummaryContract(
    Guid Id,
    int Version,
    WorkflowStatus Status,
    DateTime? PublishedAt,
    int NodeCount
);

public record WorkflowValidationContract(
    bool IsValid,
    List<WorkflowValidationErrorContract> Errors
);

public record WorkflowValidationErrorContract(
    string Code,
    string Message,
    string? NodeKey
);
