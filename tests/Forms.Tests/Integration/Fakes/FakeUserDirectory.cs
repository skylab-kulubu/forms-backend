using System.Collections.Concurrent;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Tests.Integration.Fakes;

public sealed class FakeUserDirectory : IUserDirectory
{
    private readonly ConcurrentDictionary<Guid, UserContract> _users = new();

    public UserContract Add(string fullName)
    {
        var id = Guid.NewGuid();
        var user = new UserContract(id, $"{id:N}@forms.test", fullName, null, fullName.Split(' ')[0]);
        _users[id] = user;

        return user;
    }

    public Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(_users.GetValueOrDefault(userId));

    public Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default) =>
        Task.FromResult(userIds.Distinct().Select(id => _users.GetValueOrDefault(id)).OfType<UserContract>().ToList());
}
