using Forms.Domain.Enums;
using Forms.Domain.Models;

namespace Forms.Application.Contracts.Responses;

public record ResponseContract(
    Guid Id,
    Guid FormId,
    Guid? UserId,
    Guid? ReviewerId,
    List<FormResponseSchemaItem> Schema,
    FormResponseStatus Status,
    FormRelationshipStatus Relationship,
    string? ReviewerNote,
    Guid? LinkedResponseId,
    DateTime SubmittedAt,
    DateTime? ReviewedAt
);

public record ResponseSummaryContract(
    Guid Id,
    Guid? UserId,
    FormResponseStatus Status,
    Guid? ReviewedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt
);