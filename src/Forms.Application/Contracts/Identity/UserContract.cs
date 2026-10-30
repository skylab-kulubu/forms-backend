namespace Skylab.Forms.Application.Contracts.Identity;

public record UserContract(
    Guid Id,
    string? Email,
    string? FullName,
    string? ProfilePictureUrl,
    string? FirstName = null
);

public record PublicUserContract(Guid Id, string? FullName, string? ProfilePictureUrl)
{
    public static PublicUserContract? From(UserContract? user) =>
        user is null ? null : new(user.Id, user.FullName, user.ProfilePictureUrl);
}
