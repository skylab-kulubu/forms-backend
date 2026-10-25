using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.Caching;

/// <summary>Pencereli sayaç. INCR ile PEXPIRE tek betikte: arada kopan bağlantı süresiz bir sayaç bırakmasın.</summary>
internal static class RedisCounter
{
    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    public static async Task<long> IncrementAsync(IDatabase database, string key, TimeSpan window) =>
        (long)await database.ScriptEvaluateAsync(IncrementScript, [key], [Math.Max(1L, (long)window.TotalMilliseconds)]);
}
