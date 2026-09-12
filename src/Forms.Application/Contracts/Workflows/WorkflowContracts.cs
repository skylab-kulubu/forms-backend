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

public record WorkflowNodeContract(
    string NodeKey,
    Guid FormId,
    string FormTitle,
    bool IsStart
);

public record WorkflowTransitionContract(
    string SourceNodeKey,
    string? TargetNodeKey,
    WorkflowTransitionTrigger Trigger,
    WorkflowConditionGroup? Condition,
    int Priority
);

public record WorkflowSummaryContract(
    Guid Id,
    string Name,
    WorkflowStatus Status,
    bool AllowMultipleRuns,
    int PublishedNodeCount,
    DateTime? UpdatedAt
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
