namespace Skylab.Forms.Application.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>Kalıba uyan anahtarlar; sunucuyu kilitlemeden SCAN ile gezilir.</summary>
    IAsyncEnumerable<string> ScanKeysAsync(string pattern, CancellationToken ct = default);

    /// <summary>Anahtarları siler ve gerçekten silinenlerin sayısını döner.</summary>
    Task<long> RemoveManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default);

    Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseLockAsync(string key, CancellationToken ct = default);
}
