namespace Skylab.Forms.Application.Contracts.Responses;

public record FormMailRecipientContract(string Name, string Email);

public record FormMailRecipientsContract(
    Guid FormId,
    Guid PreparedByUserId,
    string FormTitle,
    IReadOnlyList<FormMailRecipientContract> Recipients,
    int ResponseCount,
    int SkippedCount,
    int DuplicateCount
);

public record FormMailSourceContract(
    Guid SourceId,
    Guid FormId,
    Guid PreparedByUserId,
    string FormTitle,
    int RecipientCount,
    int ResponseCount,
    int SkippedCount,
    int DuplicateCount,
    DateTimeOffset PreparedAt,
    DateTimeOffset ExpiresAt
);
