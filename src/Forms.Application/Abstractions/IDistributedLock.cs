namespace Skylab.Forms.Application.Abstractions;

/// <summary>Birden çok örnek arasında bir işi tek sahibe veren kilit; kilidi yalnız onu alan bırakabilir.</summary>
public interface IDistributedLock
{
    /// <returns>Kilit başkasındaysa null.</returns>
    Task<LockLease?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Kilidi bırakır; süresi dolup başkasına geçtiyse ona dokunmaz.</summary>
    Task ReleaseAsync(LockLease lease, CancellationToken ct = default);
}

public sealed record LockLease(string Key, string Token);
