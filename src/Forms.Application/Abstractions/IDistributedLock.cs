namespace Skylab.Forms.Application.Abstractions;

public interface IDistributedLock
{
    Task<LockLease?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    Task ReleaseAsync(LockLease lease, CancellationToken ct = default);
}

public sealed record LockLease(string Key, string Token);
