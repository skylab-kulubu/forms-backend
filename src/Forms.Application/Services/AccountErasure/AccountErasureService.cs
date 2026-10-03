using System.Text.Json;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.AccountErasure;

public sealed class AccountErasureService(
    IAccountErasureRepository erasures,
    IFormsUnitOfWork unitOfWork,
    ICacheService cache) : IAccountErasureService
{
    private sealed record ShareLink(Guid ResponseId, List<Guid>? InstanceResponseIds, Guid SharedByUserId);

    public Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default) =>
        erasures.FindReceiptAsync(requestId, ct);

    public async Task<AccountErasureReceipt> EraseAsync(AccountErasureCommand command, CancellationToken ct = default)
    {
        var drafts = await DeleteDraftsAsync(command.SubjectId, ct);
        var shareLinks = await DeleteShareLinksAsync(command.SubjectId, ct);

        var receipt = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await erasures.LockAsync(command.RequestId, token);
            var existing = await erasures.FindReceiptAsync(command.RequestId, token);
            if (existing is not null) return existing;

            var counts = new SortedDictionary<string, long>(await erasures.EraseAsync(command, token), StringComparer.Ordinal)
            {
                ["drafts_deleted"] = drafts,
                ["share_links_deleted"] = shareLinks
            };
            var now = DateTime.UtcNow;
            var created = new AccountErasureReceipt
            {
                RequestId = command.RequestId,
                CompletedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond)),
                Counts = JsonSerializer.Serialize(counts)
            };
            await erasures.AddReceiptAsync(created, token);
            return created;
        }, ct);

        try { await cache.RemoveByPrefixAsync(FormCacheKeys.AnalyticsPrefix, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { }

        return receipt;
    }

    private async Task<long> DeleteDraftsAsync(Guid userId, CancellationToken ct)
    {
        long deleted = 0;
        foreach (var pattern in new[] { $"forms:draft:response:*:{userId}", $"forms:draft:form:*:{userId}" })
        {
            await foreach (var key in cache.ScanKeysAsync(pattern, ct))
            {
                await cache.RemoveAsync(key, ct);
                deleted++;
            }
        }
        return deleted;
    }

    private async Task<long> DeleteShareLinksAsync(Guid userId, CancellationToken ct)
    {
        long deleted = 0;
        await foreach (var tokenKey in cache.ScanKeysAsync(FormCacheKeys.ResponseShareTokenPrefix + "*", ct))
        {
            var link = await cache.TryGetAsync<ShareLink>(tokenKey, ct);
            if (link is null || link.SharedByUserId != userId) continue;

            var token = tokenKey[FormCacheKeys.ResponseShareTokenPrefix.Length..];
            foreach (var responseId in (link.InstanceResponseIds ?? []).Append(link.ResponseId).Distinct())
            {
                var responseKey = FormCacheKeys.ResponseShareResponsePrefix + responseId;
                if (await cache.TryGetAsync<string>(responseKey, ct) == token)
                    await cache.RemoveAsync(responseKey, ct);
            }
            await cache.RemoveAsync(tokenKey, ct);
            deleted++;
        }
        return deleted;
    }
}
