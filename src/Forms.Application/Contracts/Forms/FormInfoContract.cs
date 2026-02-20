using Forms.Domain.Enums;

namespace Forms.Application.Contracts.Forms;

public record FormInfoContract(
    Guid Id,
    string Title,
    FormStatus Status,
    DateTime UpdatedAt,
    int ResponseCount,
    int WaitingResponses,
    double? AverageTimeSeconds,
    IReadOnlyList<FormLastSeenUserContract> LastSeenUsers
);

public record FormLastSeenUserContract(
    Guid UserId,
    DateTime LastSeenAt
);
