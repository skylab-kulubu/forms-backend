using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

/// <summary>Bir işin commit'ten önce ya da sonra gelen yan etkisi başarısız olunca kaydedilmiş veri kaybolmamalı.</summary>
[Collection(ApiCollection.Name)]
public class AttemptSideEffectTests(FormsApiFactory api)
{
    /// <summary>Redis'e yanıt taslağı yazımını reddeder; geri kalanı gerçek önbelleğe gider.</summary>
    private sealed class DraftWritesFail(ICacheService inner) : ICacheService
    {
        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default) =>
            key.StartsWith("forms:draft:response:", StringComparison.Ordinal)
                ? throw new TimeoutException("Redis yanıt vermedi")
                : inner.SetAsync(key, value, expiry, ct);

        public Task<T?> GetAsync<T>(string key, TimeSpan? slidingExpiration = null, CancellationToken ct = default) => inner.GetAsync<T>(key, slidingExpiration, ct);
        public Task RemoveAsync(string key, CancellationToken ct = default) => inner.RemoveAsync(key, ct);
        public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => inner.RemoveByPrefixAsync(prefix, ct);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => inner.ExistsAsync(key, ct);
        public IAsyncEnumerable<string> ScanKeysAsync(string pattern, CancellationToken ct = default) => inner.ScanKeysAsync(pattern, ct);
        public Task<bool> AcquireLockAsync(string key, TimeSpan ttl, CancellationToken ct = default) => inner.AcquireLockAsync(key, ttl, ct);
        public Task ReleaseLockAsync(string key, CancellationToken ct = default) => inner.ReleaseLockAsync(key, ct);
        public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default) => inner.IncrementAsync(key, window, ct);
    }

    [Fact]
    public async Task An_extension_that_cannot_write_the_draft_back_keeps_the_provisional_answer()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var exam = await owner.CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        var candidate = api.ClientFor(api.Users.Add("Mehmet Kaya").Id);
        (await candidate.PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();
        (await candidate.PostAsJsonAsync("/api/forms/responses/draft", new ResponseDraftRequest(exam.Id, [new FormResponseSchemaItem { Id = "name", Answer = "Mehmet" }], 60))).EnsureSuccessStatusCode();

        Guid attemptId;
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
            attemptId = await db.Attempts.Where(a => a.FormId == exam.Id).Select(a => a.Id).SingleAsync();
            await db.Attempts.Where(a => a.Id == attemptId).ExecuteUpdateAsync(s => s.SetProperty(a => a.DeadlineAt, DateTime.UtcNow.AddMinutes(-10)));
            await scope.ServiceProvider.GetRequiredService<IFormAttemptService>().ExpireByIdAsync(attemptId);
        }

        await using var redisDown = api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            var cache = services.Last(service => service.ServiceType == typeof(ICacheService));
            services.RemoveAll<ICacheService>();
            services.AddSingleton<ICacheService>(provider => new DraftWritesFail((ICacheService)ActivatorUtilities.CreateInstance(provider, cache.ImplementationType!)));
        }));
        var ownerThroughBrokenCache = redisDown.CreateClient();
        ownerThroughBrokenCache.DefaultRequestHeaders.Authorization = owner.DefaultRequestHeaders.Authorization;

        // TestServer işlenmeyen hatayı çağırana verir; production'da bu bir 500'dür.
        var failure = await Record.ExceptionAsync(() => ownerThroughBrokenCache.PostAsJsonAsync($"/api/admin/forms/attempts/{attemptId}/extend", new { minutes = 10 }));

        using var check = api.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<FormsDbContext>();
        Assert.IsType<TimeoutException>(failure);
        Assert.Equal(FormAttemptStatus.Provisional, await checkDb.Attempts.Where(a => a.Id == attemptId).Select(a => a.Status).SingleAsync());
        Assert.True(await checkDb.Responses.AnyAsync(r => r.FormId == exam.Id && r.Status == FormResponseStatus.Provisional));
    }
}
