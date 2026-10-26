using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Caching;

public class RedisCacheService : ICacheService
{
    private readonly IDatabase _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _db = redis.GetDatabase();
        _logger = logger;
    }

    public Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default) =>
        LoggedAsync("okuma", async () =>
        {
            var value = await _db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return default;
            if (slidingExpiration.HasValue)
            {
                await _db.KeyExpireAsync(key, slidingExpiration.Value);
            }
            return JsonSerializer.Deserialize<T>(value!);
        });
    public Task SetAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct) =>
        LoggedAsync("yazma", () => _db.StringSetAsync(key, JsonSerializer.Serialize(value), expiry.HasValue ? expiry.Value : default));
    public Task RemoveAsync(string key, CancellationToken ct) =>
        LoggedAsync("silme", () => _db.KeyDeleteAsync(key));
    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct) =>
        LoggedAsync("önekle silme", async () =>
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: $"{prefix}*").ToArray();
            return keys.Length > 0 ? await _db.KeyDeleteAsync(keys) : 0;
        });
    public Task<bool> ExistsAsync(string key, CancellationToken ct) =>
        LoggedAsync("varlık", () => _db.KeyExistsAsync(key));
    public async IAsyncEnumerable<string> ScanKeysAsync(string pattern, [EnumeratorCancellation] CancellationToken ct)
    {
        var server = _redis.GetServer(_redis.GetEndPoints().First());
        await foreach (var key in server.KeysAsync(_db.Database, pattern).WithCancellation(ct))
            yield return key.ToString();
    }

    public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct) =>
        LoggedAsync("sayaç", () => RedisCounter.IncrementAsync(_db, key, window));

    /// <summary>
    /// Çağıranların çoğu cache hatasını yutar (Try* yardımcıları); Redis'in çöktüğü en azından burada görünür. Redis'in
    /// mesajı komutu ve anahtarı içerir, anahtar da bir paylaşım token'ı olabilir; yalnız işlem ve hata türü yazılır.
    /// </summary>
    private async Task<TResult> LoggedAsync<TResult>(string operation, Func<Task<TResult>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Redis {Operation} yapılamadı: {Error}", operation, ex.GetType().Name);
            throw;
        }
    }
}
