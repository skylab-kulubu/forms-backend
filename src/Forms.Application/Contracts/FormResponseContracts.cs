using Forms.Domain.Models;
using Forms.Domain.Enums;

namespace Forms.Application.Contracts;

public record FormResponseSubmitContract(
    Guid FormId,
    List<FormResponseSchemaItem> Responses
);

public record FormResponseSummaryContract(
    Guid Id,
    Guid? UserId,
    FormResponseStatus Status,
    DateTime SubmittedAt,
    DateTime? ReviewedAt
);

public record FormResponseDetailContract(
    Guid Id,
    Guid FormId,
    Guid? UserId,
    List<FormResponseSchemaItem> Schema,
    FormResponseStatus Status,
    FormRelationshipStatus Relationship,
    Guid? LinkedResponseId,
    DateTime SubmittedAt,
    DateTime? ReviewedAt
);

public record FormResponseStatusUpdateContract(
    Guid ResponseId,
    FormResponseStatus NewStatus
);