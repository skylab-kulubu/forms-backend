using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class DistributedLockTests(FormsApiFactory api)
{
    [Fact]
    public async Task An_owner_whose_lock_expired_cannot_release_the_next_owners_lock()
    {
        var locks = api.Services.GetRequiredService<IDistributedLock>();
        var key = $"test:lock:{Guid.NewGuid():N}";

        // Turu kilit süresini aşan örnek: kilit düşer, bir başkası alır, eski sahip işini bitirip bırakmaya çalışır.
        var late = await locks.TryAcquireAsync(key, TimeSpan.FromMilliseconds(100));
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        var current = await locks.TryAcquireAsync(key, TimeSpan.FromMinutes(1));
        await locks.ReleaseAsync(late!);

        Assert.NotNull(current);
        Assert.Null(await locks.TryAcquireAsync(key, TimeSpan.FromMinutes(1)));

        await locks.ReleaseAsync(current!);
        Assert.NotNull(await locks.TryAcquireAsync(key, TimeSpan.FromMinutes(1)));
    }
}
