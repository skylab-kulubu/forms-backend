using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Services;
using Skylab.Forms.Domain.Entities;
using Xunit;

namespace Forms.Application.Tests;

public class ComponentGroupLifecycleTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Delete_archives_idempotently_and_only_owner_can_list_inactive_group()
    {
        var repository = new InMemoryComponentGroupRepository();
        var service = CreateService(repository);
        var created = await service.CreateGroupAsync(
            new ComponentGroupUpsertRequest(null, "GECEKODU Başvuru", null, []),
            OwnerId);
        var groupId = created.Data!.Id;

        var firstDelete = await service.DeleteGroupAsync(groupId, OwnerId);
        var firstArchive = Assert.Single((await service.GetUserGroupsAsync(
            OwnerId,
            new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive))).Data!.Items);
        var secondDelete = await service.DeleteGroupAsync(groupId, OwnerId);
        var secondArchive = Assert.Single((await service.GetUserGroupsAsync(
            OwnerId,
            new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive))).Data!.Items);

        Assert.Equal(ServiceStatus.Success, firstDelete.Status);
        Assert.Equal(ServiceStatus.Success, secondDelete.Status);
        Assert.NotNull(firstArchive.ArchivedAt);
        Assert.Equal(firstArchive.ArchivedAt, secondArchive.ArchivedAt);
        Assert.Equal(OwnerId, secondArchive.ArchivedBy);

        var currentRead = await service.GetGroupByIdAsync(groupId, OwnerId, token: null);
        Assert.Equal(ServiceStatus.NotFound, currentRead.Status);

        var ownerArchive = await service.GetUserGroupsAsync(
            OwnerId,
            new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive));
        Assert.Single(ownerArchive.Data!.Items);
        Assert.Equal(firstArchive.ArchivedAt, ownerArchive.Data.Items[0].ArchivedAt);
        Assert.Equal(OwnerId, ownerArchive.Data.Items[0].ArchivedBy);

        var anotherUsersArchive = await service.GetUserGroupsAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive));
        Assert.Empty(anotherUsersArchive.Data!.Items);

        var ownerAll = await service.GetUserGroupsAsync(
            OwnerId,
            new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.All));
        Assert.Single(ownerAll.Data!.Items);
    }

    [Fact]
    public async Task Restore_is_owner_only_idempotent_and_returns_group_to_default_reads()
    {
        var repository = new InMemoryComponentGroupRepository();
        var service = CreateService(repository);
        var created = await service.CreateGroupAsync(
            new ComponentGroupUpsertRequest(null, "AGC Başvuru", null, []),
            OwnerId);
        var groupId = created.Data!.Id;
        await service.DeleteGroupAsync(groupId, OwnerId);

        var unauthorizedRestore = await service.RestoreGroupAsync(
            groupId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var firstRestore = await service.RestoreGroupAsync(groupId, OwnerId);
        var secondRestore = await service.RestoreGroupAsync(groupId, OwnerId);

        Assert.Equal(ServiceStatus.NotFound, unauthorizedRestore.Status);
        Assert.Equal(ServiceStatus.Success, firstRestore.Status);
        Assert.Equal(ServiceStatus.Success, secondRestore.Status);
        Assert.Null(firstRestore.Data!.ArchivedAt);
        Assert.Null(firstRestore.Data.ArchivedBy);

        var currentRead = await service.GetGroupByIdAsync(groupId, OwnerId, token: null);
        Assert.Equal(ServiceStatus.Success, currentRead.Status);

        var currentList = await service.GetUserGroupsAsync(OwnerId, new GetComponentGroupsRequest());
        Assert.Single(currentList.Data!.Items);
        var inactiveList = await service.GetUserGroupsAsync(
            OwnerId,
            new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive));
        Assert.Empty(inactiveList.Data!.Items);
    }

    private static ComponentGroupService CreateService(InMemoryComponentGroupRepository repository) =>
        new(repository, new InMemoryUnitOfWork(), new NoOpCache(), new NullUserService());

    private sealed class InMemoryComponentGroupRepository : IComponentGroupRepository
    {
        private readonly List<ComponentGroup> _groups = [];

        public Task<ComponentGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_groups.SingleOrDefault(group => group.Id == id && group.ArchivedAt == null));

        public Task<ComponentGroup?> GetForEditAsync(Guid id, CancellationToken ct = default) =>
            GetByIdAsync(id, ct);

        public Task<ComponentGroup?> GetForLifecycleEditAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_groups.SingleOrDefault(group => group.Id == id));

        public Task<PagedResult<ComponentGroupContract>> GetUserGroupsAsync(
            Guid userId,
            GetComponentGroupsRequest request,
            CancellationToken ct = default)
        {
            var query = _groups.Where(group => group.OwnedBy == userId);
            query = request.Lifecycle switch
            {
                ComponentGroupLifecycleVisibility.Current => query.Where(group => group.ArchivedAt == null),
                ComponentGroupLifecycleVisibility.Inactive => query.Where(group => group.ArchivedAt != null),
                ComponentGroupLifecycleVisibility.All => query,
                _ => throw new ArgumentOutOfRangeException(nameof(request.Lifecycle))
            };
            var items = query
                .Select(group => new ComponentGroupContract(
                    group.Id,
                    group.Title,
                    group.Description,
                    group.Schema,
                    ArchivedAt: group.ArchivedAt,
                    ArchivedBy: group.ArchivedBy))
                .ToList();
            return Task.FromResult(new PagedResult<ComponentGroupContract>(items, items.Count, 1, 10));
        }

        public void Add(ComponentGroup group) => _groups.Add(group);
    }

    private sealed class InMemoryUnitOfWork : IFormsUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);
    }

    private sealed class NoOpCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default) =>
            Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task ReleaseLockAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullUserService : IExternalUserService
    {
        public Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<UserContract?>(null);

        public Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default) =>
            Task.FromResult(new List<UserContract>());
    }
}
