using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.GuestUploads;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.GuestUploads;

public sealed class RedisGuestUploadStore : IGuestUploadStore
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumLifetime = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string IncrementScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then redis.call('PEXPIRE', KEYS[1], ARGV[1]) end
        return count
        """;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisGuestUploadStore> _logger;

    public RedisGuestUploadStore(IConnectionMultiplexer redis, ILogger<RedisGuestUploadStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<long> CountAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var milliseconds = Math.Max(1L, (long)window.TotalMilliseconds);
        var count = await RunAsync("count", database => database.ScriptEvaluateAsync(IncrementScript, [key], [milliseconds]), ct);
        return (long)count;
    }

    public Task SaveSessionAsync(GuestUploadSession session, CancellationToken ct = default) =>
        SetAsync("save-session", GuestUploadKeys.Session(session.Id), session, session.ExpiresAt, ct);

    public Task<GuestUploadSession?> FindSessionAsync(string sessionId, CancellationToken ct = default) =>
        GetAsync<GuestUploadSession>("find-session", GuestUploadKeys.Session(sessionId), ct);

    public Task SaveFileAsync(string sessionId, GuestUploadedFile file, DateTime expiresAt, CancellationToken ct = default) =>
        SetAsync("save-file", GuestUploadKeys.SessionFile(sessionId, file.MediaId), file, expiresAt, ct);

    public Task<GuestUploadedFile?> FindFileAsync(string sessionId, Guid mediaId, CancellationToken ct = default) =>
        GetAsync<GuestUploadedFile>("find-file", GuestUploadKeys.SessionFile(sessionId, mediaId), ct);

    public async Task RemoveFilesAsync(string sessionId, IReadOnlyCollection<Guid> mediaIds, CancellationToken ct = default)
    {
        if (mediaIds.Count == 0) return;

        RedisKey[] keys = [.. mediaIds.Select(mediaId => (RedisKey)GuestUploadKeys.SessionFile(sessionId, mediaId))];
        await RunAsync("remove-files", database => database.KeyDeleteAsync(keys), ct);
    }

    private async Task SetAsync<T>(string operation, string key, T value, DateTime expiresAt, CancellationToken ct)
    {
        var lifetime = expiresAt - DateTime.UtcNow;
        var json = JsonSerializer.Serialize(value, Json);

        await RunAsync(operation, database => database.StringSetAsync(key, json, lifetime > MinimumLifetime ? lifetime : MinimumLifetime), ct);
    }

    private async Task<T?> GetAsync<T>(string operation, string key, CancellationToken ct) where T : class
    {
        var value = await RunAsync(operation, database => database.StringGetAsync(key), ct);
        if (value.IsNullOrEmpty) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(value.ToString(), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<T> RunAsync<T>(string operation, Func<IDatabase, Task<T>> command, CancellationToken ct)
    {
        try
        {
            return await command(_redis.GetDatabase()).WaitAsync(OperationTimeout, ct);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or ObjectDisposedException
                                   || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Misafir yükleme deposuna ulaşılamadı ({Operation}, {Error})", operation, ex.GetType().Name);
            throw new GuestUploadStoreUnavailableException(ex);
        }
    }
}
