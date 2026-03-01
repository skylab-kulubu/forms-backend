namespace Forms.Application.Contracts.ComponentGroup;

public record GetComponentGroupsRequest(
    int Page = 1,
    int PageSize = 10,
    string? Search = null,
    string SortDirection = "descending"
);
