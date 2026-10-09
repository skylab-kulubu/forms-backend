namespace Skylab.Forms.Application.Abstractions;

public static class CacheServiceExtensions
{
    public static async Task<T?> TryGetAsync<T>(this ICacheService cache, string key, CancellationToken ct = default)
    {
        try { return await cache.GetAsync<T>(key, ct: ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return default; }
    }

    public static async Task TrySetAsync<T>(this ICacheService cache, string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        try { await cache.SetAsync(key, value, expiry, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    public static async Task TryRemoveAsync(this ICacheService cache, string key, CancellationToken ct = default)
    {
        try { await cache.RemoveAsync(key, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }
    }

    public static async Task<long?> TryIncrementAsync(this ICacheService cache, string key, TimeSpan window, CancellationToken ct = default)
    {
        try { return await cache.IncrementAsync(key, window, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }
}
