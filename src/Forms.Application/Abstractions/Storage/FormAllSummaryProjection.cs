using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Abstractions.Storage;

public record FormAllSummaryProjection(
    Guid Id,
    string Title,
    FormStatus Status,
    Guid OwnerUserId,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    int ResponseCount
);
