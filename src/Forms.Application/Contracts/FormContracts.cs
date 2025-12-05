using Forms.Domain.Enums;
using Forms.Domain.Models;

namespace Forms.Application.Contracts;

public record FormUpsertContract(
    Guid? Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    Guid? LinkedFormId,
    FormStatus Status,
    List<FormCollaboratorUpsertContract>? Collaborators
);

public record FormContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    FormStatus Status,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    Guid? LinkedFormId,
    List<FormCollaboratorContract> Collaborators,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record FormSummaryContract(
    Guid Id,
    string Title,
    string? Description,
    FormStatus Status,
    Guid? LinkedFormId,
    DateTime? UpdatedAt,
    int ResponseCount
);
public record FormCollaboratorContract(
    Guid UserId,
    CollaboratorRole Role
);
public record FormCollaboratorUpsertContract(
    Guid UserId, 
    CollaboratorRole Role
);