namespace Skylab.Forms.Application.Contracts.Identity;

public record UserContract(
    Guid Id,
    string? Email,
    string? FullName,
    string? ProfilePictureUrl,
    string? FirstName = null
);
