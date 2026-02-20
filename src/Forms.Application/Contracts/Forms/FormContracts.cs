using Forms.Domain.Enums;
using Forms.Domain.Models;
using Forms.Application.Contracts.Collaborators;

namespace Forms.Application.Contracts.Forms;

public record FormContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    FormStatus Status,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    LinkedFormContract? LinkedForm,
    bool IsChildForm,
    CollaboratorRole userRole,
    List<FormCollaboratorContract> Collaborators,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record FormDisplayContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema
);

public record FormSummaryContract(
    Guid Id,
    string Title,
    FormStatus Status,
    LinkedFormContract? LinkedForm,
    CollaboratorRole UserRole,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    DateTime? UpdatedAt,
    int ResponseCount
);

public record LinkedFormContract( 
    Guid Id,
    string Title
);

public record LinkableFormsContract(
    Guid Id,
    string Title
);