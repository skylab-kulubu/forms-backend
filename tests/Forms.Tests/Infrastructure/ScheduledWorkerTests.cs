using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Scheduling;

namespace Skylab.Forms.Tests.Infrastructure;

public class ScheduledWorkerTests
{
    private sealed class LockOnlyCache(bool grants) : ICacheService
    {
        public int Releases { get; private set; }

        public Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(grants);

        public Task ReleaseLockAsync(string key, CancellationToken ct = default)
        {
            Releases++;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<string> ScanKeysAsync(string pattern, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>İlk turu patlar, üçüncü turda haber verir.</summary>
    private sealed class FlakyWorker(ICacheService cache, bool releases) : ScheduledWorker(cache, NullLogger.Instance)
    {
        public int Rounds { get; private set; }
        public TaskCompletionSource ThirdRound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override string LockKey => "test:lock";
        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(20);
        protected override bool ReleasesLock => releases;

        protected override Task RunAsync(CancellationToken ct)
        {
            if (++Rounds == 3) ThirdRound.TrySetResult();
            return Rounds == 1 ? throw new InvalidOperationException("ilk tur") : Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_failing_round_does_not_stop_the_next_and_the_lock_is_released_only_when_asked(bool releases)
    {
        var cache = new LockOnlyCache(grants: true);
        var worker = new FlakyWorker(cache, releases);

        await worker.StartAsync(CancellationToken.None);
        await worker.ThirdRound.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.Rounds >= 3);
        Assert.Equal(releases, cache.Releases >= 3);
        Assert.Equal(!releases, cache.Releases == 0);
    }

    [Fact]
    public async Task Without_the_lock_a_round_does_not_run()
    {
        var worker = new FlakyWorker(new LockOnlyCache(grants: false), releases: true);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, worker.Rounds);
    }
}
