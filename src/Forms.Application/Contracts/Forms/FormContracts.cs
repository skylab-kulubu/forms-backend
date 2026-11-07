using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Common;
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
    FormTask? Task = null,
    DateTime? ClosesAt = null,
    int? TimeLimitMinutes = null,
    int PublishedVersion = 1,
    FormAccessContract? Access = null
);

public record FormAccessContract(bool CanViewResponses, bool CanHandleResponses, bool CanEdit);

public record FormDisplayContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    bool RequiresManualReview = false,
    FormTask? Task = null,
    DateTime? ClosesAt = null,
    int? TimeLimitMinutes = null,
    bool AllowAnonymousResponses = false
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
    int PublishedVersion = 1
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
    int PendingCount = 0,
    DateTime? LastResponseAt = null,
    bool IsOrphaned = false
);

public record FormCatalogSummaryContract(int Total, int Open, int Closed, int Deleted, int Orphaned, int Pending);

public record FormOwnerContract(Guid Id, string? Email, string? FullName, string? ProfilePictureUrl, string? FirstName, int FormCount);

public record FormAllPageContract(
    List<FormAllSummaryContract> Items,
    int TotalCount,
    int Page,
    int PageSize,
    FormCatalogSummaryContract Summary,
    List<FormOwnerContract> Owners
) : PagedResult<FormAllSummaryContract>(Items, TotalCount, Page, PageSize);

public record FormWorkflowRefContract(
    Guid Id,
    string Name,
    bool IsStart,
    bool IsPublished,
    bool AllowMultipleRuns,
    WorkflowIntake Intake,
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
