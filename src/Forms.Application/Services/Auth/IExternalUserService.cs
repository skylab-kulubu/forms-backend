using Forms.Application.Contracts.Auth;

namespace Forms.Application.Services;

public interface IExternalUserService
{
    Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);
}