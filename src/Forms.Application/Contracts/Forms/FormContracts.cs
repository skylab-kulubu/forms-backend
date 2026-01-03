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
    Guid? LinkedFormId,
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
    Guid? LinkedFormId,
    CollaboratorRole UserRole,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    DateTime? UpdatedAt,
    int ResponseCount
);

public record LinkableFormsContract(
    Guid Id,
    string Title
);