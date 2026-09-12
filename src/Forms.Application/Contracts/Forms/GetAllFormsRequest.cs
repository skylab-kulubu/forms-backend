namespace Skylab.Forms.Application.Contracts.Forms;

public record GetAllFormsRequest(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? AllowAnonymous = null,
    bool? AllowMultiple = null,
    bool? RequiresManualReview = null,
    string SortDirection = "descending"
);
