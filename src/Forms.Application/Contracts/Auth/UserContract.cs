namespace Forms.Application.Contracts.Auth;

public record UserContract(
    Guid Id,
    string? Email
);