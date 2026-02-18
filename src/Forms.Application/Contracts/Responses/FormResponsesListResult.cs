namespace Forms.Application.Contracts.Responses;

public record FormResponsesListResult(
    PagedResult<ResponseSummaryContract> PaginationData,
    double? AverageTimeSpent
);