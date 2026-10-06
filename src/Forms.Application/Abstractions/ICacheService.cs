namespace Skylab.Forms.Application.Abstractions;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseLockAsync(string key, CancellationToken ct = default);

    /// <summary>Sayacı artırır; ilk artış <paramref name="window"/> süren bir pencere açar, pencere bitince sayaç sıfırdan başlar.</summary>
    Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default);
}
