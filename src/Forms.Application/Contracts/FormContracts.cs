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
    FormStatus Status
);

public record FormContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    FormStatus Status,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record FormSummaryContract(
    Guid Id,
    string Title,
    string? Description,
    FormStatus Status,
    DateTime? UpdatedAt,
    int ResponseCount
);