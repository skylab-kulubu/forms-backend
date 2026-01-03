using Forms.Domain.Enums;

namespace Forms.Application.Contracts.Responses;

public record ResponseStatusUpdateRequest(
    Guid ResponseId,
    FormResponseStatus NewStatus,
    string? Note
);