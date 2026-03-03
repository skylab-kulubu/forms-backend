using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Responses;

public record ResponseStatusUpdateRequest(
    Guid ResponseId,
    FormResponseStatus NewStatus,
    string? Note
);