using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Services;
using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Infrastructure.Storage.Repositories;
using Testcontainers.PostgreSql;
using Xunit;

namespace Forms.Infrastructure.Tests;

public sealed class ComponentGroupLifecycleContractTests : IAsyncLifetime
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task PostgreSql_contract_archives_filters_and_restores_component_group()
    {
        await using (var setupContext = CreateContext())
            await setupContext.Database.MigrateAsync();

        var cache = new InMemoryCache();
        Guid groupId;
        string shareToken;

        await using (var writeContext = CreateContext())
        {
            var service = CreateService(writeContext, cache);
            var created = await service.CreateGroupAsync(
                new ComponentGroupUpsertRequest(null, "GECEKODU Başvuru", "2026", []),
                OwnerId);
            groupId = created.Data!.Id;
            shareToken = (await service.CreateOrRefreshShareTokenAsync(groupId, OwnerId)).Data!.Token;

            Assert.Equal(ServiceStatus.Success, (await service.DeleteGroupAsync(groupId, OwnerId)).Status);
            Assert.Equal(ServiceStatus.Success, (await service.DeleteGroupAsync(groupId, OwnerId)).Status);
        }

        await using (var archivedContext = CreateContext())
        {
            var service = CreateService(archivedContext, cache);

            Assert.Equal(
                ServiceStatus.NotFound,
                (await service.GetGroupByIdAsync(groupId, OwnerId, token: null)).Status);
            Assert.Equal(
                ServiceStatus.NotFound,
                (await service.GetGroupMetaAsync(groupId, shareToken)).Status);

            var current = await service.GetUserGroupsAsync(OwnerId, new GetComponentGroupsRequest());
            Assert.Empty(current.Data!.Items);

            var inactive = await service.GetUserGroupsAsync(
                OwnerId,
                new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive));
            var archived = Assert.Single(inactive.Data!.Items);
            Assert.NotNull(archived.ArchivedAt);
            Assert.Equal(OwnerId, archived.ArchivedBy);

            var anotherUsersInactive = await service.GetUserGroupsAsync(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive));
            Assert.Empty(anotherUsersInactive.Data!.Items);

            var all = await service.GetUserGroupsAsync(
                OwnerId,
                new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.All));
            Assert.Single(all.Data!.Items);

            Assert.Equal(ServiceStatus.Success, (await service.RestoreGroupAsync(groupId, OwnerId)).Status);
            Assert.Equal(ServiceStatus.Success, (await service.RestoreGroupAsync(groupId, OwnerId)).Status);
        }

        await using (var restoredContext = CreateContext())
        {
            var service = CreateService(restoredContext, cache);
            Assert.Equal(
                ServiceStatus.Success,
                (await service.GetGroupByIdAsync(groupId, OwnerId, token: null)).Status);
            Assert.Single((await service.GetUserGroupsAsync(OwnerId, new GetComponentGroupsRequest())).Data!.Items);
            Assert.Empty((await service.GetUserGroupsAsync(
                OwnerId,
                new GetComponentGroupsRequest(Lifecycle: ComponentGroupLifecycleVisibility.Inactive))).Data!.Items);
        }
    }

    private FormsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FormsDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new FormsDbContext(options);
    }

    private static ComponentGroupService CreateService(FormsDbContext context, ICacheService cache) =>
        new(
            new ComponentGroupRepository(context),
            new FormsUnitOfWork(context),
            cache,
            new NullUserService());

    private sealed class InMemoryCache : ICacheService
    {
        private readonly Dictionary<string, object?> _values = [];

        public Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        {
            foreach (var key in _values.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                _values.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.ContainsKey(key));

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
