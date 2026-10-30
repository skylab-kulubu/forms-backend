using Skylab.Forms.Application.Abstractions;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.Caching;

internal sealed class RedisDistributedLock(IConnectionMultiplexer redis) : IDistributedLock
{
    private const string ReleaseScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end
        """;

    public async Task<LockLease?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        var token = Guid.NewGuid().ToString("N");

        return await redis.GetDatabase().StringSetAsync(key, token, ttl, When.NotExists) ? new LockLease(key, token) : null;
    }

    public Task ReleaseAsync(LockLease lease, CancellationToken ct = default) =>
        redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [lease.Key], [lease.Token]);
}
