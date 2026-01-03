using Forms.Domain.Enums;

namespace Forms.Application.Contracts.Responses;

public record GetResponsesRequest(
    int Page = 1,
    int PageSize = 15,
    FormResponseStatus? Status = null,
    FormResponderType ResponderType = FormResponderType.All,
    Guid? FilterByUserId = null,
    string SortingDirection = "descending"  
);