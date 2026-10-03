using System.Text.Json;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.AccountErasure;

namespace Skylab.Forms.Application.Services.AccountErasure;

public sealed class AccountErasureService(IAccountErasureRepository erasures, ICacheService cache) : IAccountErasureService
{
    /// <summary>Bir kişi için aranacak en çok tam ad; kişi başına birkaç etkinlik yanıtı olur.</summary>
    private const int MaxNames = 16;

    public Task<AccountErasureReceiptContract?> FindCompletedAsync(Guid requestId, CancellationToken ct = default) =>
        erasures.FindReceiptAsync(requestId, ct);

    public async Task<AccountErasureReceiptContract> EraseAsync(AccountErasureCommand command, CancellationToken ct = default)
    {
        var responses = await erasures.GetSubjectResponsesAsync(command, ct);
        var responseIds = responses.Select(response => response.ResponseId).ToHashSet();

        // Redis transaction'a girmez; önce o silinir. İkinci koşuda silinecek bir şey
        // kalmadığı için bu adım da tekrarlanabilir.
        var cacheCounts = new Dictionary<string, long>
        {
            ["drafts_deleted"] = await DeleteDraftsAsync(command.SubjectId, ct),
            ["share_links_deleted"] = await DeleteShareLinksAsync(command.SubjectId, responseIds, ct)
        };

        return await erasures.EraseAsync(new AccountErasureWork(command, FullNames(responses), cacheCounts), ct);
    }

    /// <summary>
    /// Kişinin yanıtlarındaki ad ve soyad cevaplarından tam adlar. Başkasının yanıtında
    /// ya da inceleme notunda geçen ad bu adlarla aranır; tek kelimelik ad aranmaz.
    /// </summary>
    private static List<string> FullNames(IReadOnlyList<AccountErasureSubjectResponse> responses)
    {
        var names = new List<string>();
        foreach (var response in responses)
        {
            var identity = EventIdentity.Extract(response.Schema, response.Answers);
            if (identity is null) continue;

            var name = string.Join(' ', $"{identity.FirstName} {identity.LastName}"
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (name.Length < 5 || !name.Contains(' ')) continue;
            if (names.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

            names.Add(name);
            if (names.Count == MaxNames) break;
        }
        return names;
    }

    private async Task<long> DeleteDraftsAsync(Guid subjectId, CancellationToken ct)
    {
        var keys = new List<string>();
        foreach (var pattern in FormCacheKeys.UserDraftPatterns(subjectId))
        {
            await foreach (var key in cache.ScanKeysAsync(pattern, ct))
                keys.Add(key);
        }
        return await cache.RemoveManyAsync(keys, ct);
    }

    /// <summary>
    /// Kişinin paylaştığı ya da kişinin yanıtlarını gösteren paylaşım bağlantıları.
    /// Yanıtın bağlantı kaydı yalnız silinen bağlantıyı gösteriyorsa silinir.
    /// </summary>
    private async Task<long> DeleteShareLinksAsync(Guid subjectId, HashSet<Guid> responseIds, CancellationToken ct)
    {
        var tokenKeys = new List<string>();
        var responseKeys = new List<string>();

        await foreach (var tokenKey in cache.ScanKeysAsync(FormCacheKeys.ResponseShareTokenPrefix + "*", ct))
        {
            ResponseShareEntry? entry;
            try
            {
                entry = await cache.GetAsync<ResponseShareEntry>(tokenKey, ct: ct);
            }
            catch (JsonException)
            {
                continue;
            }
            if (entry is null) continue;

            var sharedIds = (entry.InstanceResponseIds ?? []).Append(entry.ResponseId).Distinct().ToList();
            if (entry.SharedByUserId != subjectId && !sharedIds.Any(responseIds.Contains)) continue;

            tokenKeys.Add(tokenKey);
            var token = tokenKey[FormCacheKeys.ResponseShareTokenPrefix.Length..];
            foreach (var id in sharedIds)
            {
                var responseKey = FormCacheKeys.ResponseShareResponsePrefix + id;
                string? current;
                try
                {
                    current = await cache.GetAsync<string>(responseKey, ct: ct);
                }
                catch (JsonException)
                {
                    continue;
                }
                if (current == token)
                    responseKeys.Add(responseKey);
            }
        }

        var deleted = await cache.RemoveManyAsync(tokenKeys, ct);
        await cache.RemoveManyAsync(responseKeys, ct);
        return deleted;
    }
}
