using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Contracts.Forms;

public record FormContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    FormStatus Status,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    FormWorkflowRefContract? Workflow,
    CollaboratorRole userRole,
    List<FormCollaboratorContract> Collaborators,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    EventRefContract? Event = null
);

    public record FormDisplayContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    Guid? EventId = null
);

public record FormSummaryContract(
    Guid Id,
    string Title,
    FormStatus Status,
    CollaboratorRole UserRole,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    FormWorkflowRefContract? Workflow,
    DateTime? UpdatedAt,
    int ResponseCount,
    Guid? EventId,
    EventRefContract? Event = null
);

public record FormAllSummaryContract(
    Guid Id,
    string Title,
    FormStatus Status,
    UserContract CreatedBy,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    FormWorkflowRefContract? Workflow,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int ResponseCount,
    EventRefContract? Event = null
);

public record FormWorkflowRefContract(
    Guid Id,
    string Name,
    bool IsStart,
    bool IsPublished,
    bool AllowMultipleRuns,
    bool RequiresManualReview,
    List<FormLockedQuestionContract> LockedQuestions
);

public record FormLockedQuestionContract(
    string Id,
    List<string> Values
);

public record FormMetaContract(
    string Title,
    string? Description
);

public record EventRefContract(
    Guid Id,
    string? Name
);
