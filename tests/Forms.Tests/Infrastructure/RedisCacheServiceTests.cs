using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Caching;
using StackExchange.Redis;

namespace Skylab.Forms.Tests.Infrastructure;

public class RedisCacheServiceTests
{
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task A_swallowed_cache_failure_still_leaves_a_warning_without_the_key()
    {
        // 1. port kapalı; abortConnect=false bağlantıyı açılışta düşürmez, her komut hemen hata verir.
        using var redis = await ConnectionMultiplexer.ConnectAsync("127.0.0.1:1,abortConnect=false,connectTimeout=200,connectRetry=0");
        var logger = new RecordingLogger<RedisCacheService>();
        var cache = new RedisCacheService(redis, logger);

        var value = await cache.TryGetAsync<string>("forms:share:gizli-token");

        Assert.Null(value);
        var warning = Assert.Single(logger.Warnings);
        Assert.DoesNotContain("gizli-token", warning);
    }
}
