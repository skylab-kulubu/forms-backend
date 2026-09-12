using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Contracts.Responses;

public record ResponseContract(
    Guid Id,
    Guid FormId,
    UserContract? User,
    UserContract? Reviewer,
    UserContract? Archiver,
    List<FormResponseSchemaItem> Schema,
    int? TimeSpent,
    FormResponseStatus Status,
    bool IsArchived,
    ResponseWorkflowContract? Workflow,
    string? ReviewerNote,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt,
    UserContract? SharedBy = null
);

/// <summary>
/// Cevabın ait olduğu başvurunun tamamı. İnceleyen, başvuranın önceki adımlardaki
/// cevaplarını buradan görür.
/// </summary>
public record ResponseWorkflowContract(
    Guid InstanceId,
    int Stage,
    List<ResponseWorkflowStepContract> Steps
);

public record ResponseWorkflowStepContract(
    int Stage,
    string FormTitle,
    Guid? ResponseId,
    FormResponseStatus? Status
);

public record ResponseMetaContract(string FormTitle, UserContract? SharedBy);

/// <param name="LinkedFormId">Legacy bağlı form akışının hedefi; akış motorunda null.</param>
/// <param name="Step">Legacy 1..5 aşaması; akış motorunda 0.</param>
public record ResponseSubmitResult(
    Guid ResponseId,
    Guid? LinkedFormId,
    int Step,
    Guid? InstanceId = null,
    WorkflowActionState? State = null,
    int Stage = 0,
    Guid? NextFormId = null
);

public record ResponseSummaryContract(
    Guid Id,
    UserContract? User,
    FormResponseStatus Status,
    bool IsArchived,
    UserContract? ReviewedBy,
    Guid? ArchivedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt
);
