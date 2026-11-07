using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Forms;

public record GetAllFormsRequest(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    bool? AllowAnonymous = null,
    bool? AllowMultiple = null,
    bool? RequiresManualReview = null,
    string SortDirection = "descending",
    FormStatus? Status = null,
    string? Owner = null,
    string? SortBy = null
)
{
    public Guid? OwnerId =>
        string.Equals(Owner, "orphaned", StringComparison.OrdinalIgnoreCase) ? DeletedUser.Id
        : Guid.TryParse(Owner, out var id) ? id
        : null;
}
