using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Application.Contracts.Responses;

public record FormResponsesListResult(
    PagedResult<ResponseSummaryContract> PaginationData,
    double? AverageTimeSpent,
    ResponseStatusCountsContract? Counts = null,
    double? AverageTaskSeconds = null,
    bool HasTimeLimit = false
);
