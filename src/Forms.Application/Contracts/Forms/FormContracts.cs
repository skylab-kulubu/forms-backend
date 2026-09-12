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
    CollaboratorRole UserRole,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    DateTime? UpdatedAt,
    int ResponseCount
);

public record FormAllSummaryContract(
    Guid Id,
    string Title,
    FormStatus Status,
    UserContract CreatedBy,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int ResponseCount
);

public record FormMetaContract(
    string Title,
    string? Description
);
