using Forms.Domain.Enums;
using Forms.Domain.Models;
using Forms.Application.Contracts.Auth;

namespace Forms.Application.Contracts.Responses;

public record ResponseContract(
    Guid Id,
    Guid FormId,
    UserContract? User,
    UserContract? Reviewer,
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
    UserContract? User,
    FormResponseStatus Status,
    Guid? ReviewedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt
);