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
    Guid? ReviewedBy,
    DateTime SubmittedAt,
    DateTime? ReviewedAt
);

public record FormResponseDetailContract(
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

public record FormResponseStatusUpdateContract(
    Guid ResponseId,
    FormResponseStatus NewStatus,
    string? Note
);

public record GetFormResponsesRequest(
    int Page = 1,
    int PageSize = 15,
    FormResponseStatus? Status = null,
    FormResponderType ResponderType = FormResponderType.All,
    Guid? FilterByUserId = null,
    string SortingDirection = "descending"  
);