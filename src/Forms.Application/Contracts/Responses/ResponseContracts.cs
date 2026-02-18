using Forms.Domain.Enums;
using Forms.Domain.Models;
using Forms.Application.Contracts.Auth;

namespace Forms.Application.Contracts.Responses;

public record ResponseContract(
    Guid Id,
    Guid FormId,
    UserContract? User,
    UserContract? Reviewer,
    UserContract? Archiver,
    List<FormResponseSchemaItem> Schema,
    int? TimeSpent,
    FormResponseStatus Status,
    bool IsArchived,
    FormRelationshipStatus Relationship,
    string? ReviewerNote,
    Guid? LinkedResponseId,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt
);

public record ResponseSummaryContract(
    Guid Id,
    UserContract? User,
    FormResponseStatus Status,
    bool IsArchived,
    Guid? ReviewedBy,
    Guid? ArchivedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt
);