using System.Runtime.CompilerServices;
using StackExchange.Redis;
using System.Text.Json;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _redis;

    public RedisCacheService(IConnectionMultiplexer redis)
    {
        _redis = redis;
        _db = redis.GetDatabase();
    }

    public async Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default)
    {
        var value = await _db.StringGetAsync(key);
        if (value.IsNullOrEmpty) return default;
        if (slidingExpiration.HasValue)
        {
            await _db.KeyExpireAsync(key, slidingExpiration.Value);
        }
        return JsonSerializer.Deserialize<T>(value!);
    }
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value);
        await _db.StringSetAsync(key, json, expiry.HasValue ? expiry.Value : default);
    }
    public async Task RemoveAsync(string key, CancellationToken ct)
    {
        await _db.KeyDeleteAsync(key);
    }
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        var keys = server.Keys(pattern: $"{prefix}*").ToArray();
        if (keys.Length > 0)
            await _db.KeyDeleteAsync(keys);
    }
    public async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        return await _db.KeyExistsAsync(key);
    }
    public async IAsyncEnumerable<string> ScanKeysAsync(string pattern, [EnumeratorCancellation] CancellationToken ct)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        await foreach (var key in server.KeysAsync(_db.Database, pattern).WithCancellation(ct))
            yield return key.ToString();
    }

    public async Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct)
    {
        return await _db.StringSetAsync(key, "1", ttl, When.NotExists);
    }

    public async Task ReleaseLockAsync(string key, CancellationToken ct)
    {
        await _db.KeyDeleteAsync(key);
    }

}
