using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Abstractions;

public interface IUserDirectory
{
    Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);

    Task<UserLookup> FindUserAsync(Guid userId, CancellationToken ct = default);
}

public enum UserLookupOutcome
{
    Found,
    NotFound,
    Unavailable
}

public sealed record UserLookup(UserLookupOutcome Outcome, UserContract? User = null);
