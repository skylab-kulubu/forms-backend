using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Auth;

namespace Skylab.Forms.Application.Contracts.Responses;

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
    UserContract? ReviewedBy,
    Guid? ArchivedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt,
    DateTime? ArchivedAt
);