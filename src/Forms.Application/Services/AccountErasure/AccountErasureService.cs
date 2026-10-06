using System.Text.Json;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.AccountErasure;

public sealed class AccountErasureService : IAccountErasureService
{
    private readonly IAccountErasureRepository _erasures;
    private readonly IFormsUnitOfWork _uow;
    private readonly ICacheService _cache;

    public AccountErasureService(IAccountErasureRepository erasures, IFormsUnitOfWork uow, ICacheService cache)
    {
        _erasures = erasures;
        _uow = uow;
        _cache = cache;
    }

    public Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default) =>
        _erasures.FindReceiptAsync(requestId, ct);

    public async Task<AccountErasureReceipt> EraseAsync(AccountErasureCommand command, CancellationToken ct = default)
    {
        var userId = command.SubjectId;

        // Redis transaction'a girmez, bu yüzden önce silinir; tekrarında silinecek bir şey kalmaz.
        var drafts = await DeleteDraftsAsync(userId, ct);
        var shareLinks = await DeleteShareLinksAsync(userId, ct);

        var receipt = await _uow.ExecuteInTransactionAsync(async token =>
        {
            // Aynı komutun eşzamanlı ikinci çağrısı burada bekler, sonra ilkinin makbuzunu bulur.
            await _erasures.LockAsync(command.RequestId, token);
            var existing = await _erasures.FindReceiptAsync(command.RequestId, token);
            if (existing is not null) return existing;

            // Kişinin yanıtları: kendi hesabıyla gönderdikleri ve kimlik e-postası adreslerinden
            // birine eşit olan misafir yanıtları. Adres yalnız başka bir alanda geçiyorsa (ör. takım
            // arkadaşının e-postası) yanıt kişinin sayılmaz; o cevap aşağıda adres taraması ile boşalır.
            var candidates = command.Emails.Count == 0 ? [] : await _erasures.FindGuestResponseCandidatesAsync(command.Emails, token);
            var owned = (await _erasures.GetResponsesAsync(userId, candidates, token))
                .Select(response => new { response.Id, response.UserId, Identity = EventIdentity.Extract(response.Schema, response.Data) })
                .Where(response => response.UserId == userId ||
                                   (response.Identity is not null && command.Emails.Contains(response.Identity.Email.ToLowerInvariant())))
                .ToList();

            var guestResponseIds = owned.Where(response => response.UserId is null).Select(response => response.Id).ToList();

            // Tam ad, kişinin kendi yanıtlarındaki etkinlik kimliğinden okunur.
            var fullNames = owned
                .Where(response => response.Identity is not null)
                .Select(response => $"{response.Identity!.FirstName} {response.Identity.LastName}")
                .Distinct()
                .ToList();

            var counts = new Dictionary<string, long>
            {
                ["responses_redacted"] = await _erasures.RedactResponsesAsync(userId, DeletedUser.Id, token),
                ["guest_responses_redacted"] = await _erasures.RedactGuestResponsesAsync(guestResponseIds, token),
                ["attempts_deleted"] = await _erasures.DeleteAttemptsAsync(userId, token),
                ["answer_rows_cleared"] = 0,
                ["review_notes_cleared"] = 0
            };

            // Başkalarının cevaplarında ve notlarında kişiyi anan metin bütünüyle silinir.
            if (command.Emails.Count > 0 || fullNames.Count > 0)
            {
                counts["answer_rows_cleared"] = await _erasures.ClearAnswerMentionsAsync(command.Emails, fullNames, token);
                counts["review_notes_cleared"] = await _erasures.ClearNoteMentionsAsync(command.Emails, fullNames, token);
            }

            counts["actor_columns_replaced"] = await _erasures.ReplaceActorColumnsAsync(userId, DeletedUser.Id, token);

            // Owner satırı silinmez, Silinmiş kullanıcıya geçer: her formun tek sahibi kalır,
            // kimse kendiliğinden sahip olmaz. Diğer collaborator satırları silinir.
            counts["collaborators_deleted"] = await _erasures.RemoveCollaboratorsAsync(userId, token);
            counts["form_owners_replaced"] = await _erasures.ReplaceOwnersAsync(userId, DeletedUser.Id, token);

            // Açık başvuru biter ve açık adımı kapanır; böylece yer tutucunun aktif başvurusu olmaz.
            counts["workflow_runs_cancelled"] = await _erasures.CloseWorkflowRunsAsync(userId, DateTime.UtcNow, token);
            counts["workflow_runs_detached"] = await _erasures.DetachWorkflowRunsAsync(userId, DeletedUser.Id, token);

            counts["drafts_deleted"] = drafts;
            counts["share_links_deleted"] = shareLinks;

            var created = new AccountErasureReceipt
            {
                RequestId = command.RequestId,
                CompletedAt = DateTime.UtcNow,
                Counts = JsonSerializer.Serialize(counts)
            };
            _erasures.Add(created);
            await _uow.SaveChangesAsync(token);

            return created;
        }, ct);

        // Önbellekteki form analizleri silinen cevapları içerebilir; sonraki okumada yeniden hesaplanır.
        await _cache.TryRemoveByPrefixAsync(FormCacheKeys.AnalyticsPrefix, ct);

        return receipt;
    }

    private async Task<long> DeleteDraftsAsync(Guid userId, CancellationToken ct)
    {
        long deleted = 0;
        foreach (var pattern in FormCacheKeys.UserDraftPatterns(userId))
        {
            await foreach (var key in _cache.ScanKeysAsync(pattern, ct))
            {
                await _cache.RemoveAsync(key, ct);
                deleted++;
            }
        }

        return deleted;
    }

    /// <summary>Kişinin açtığı yanıt paylaşım bağlantıları ve onları gösteren yanıt anahtarları.</summary>
    private async Task<long> DeleteShareLinksAsync(Guid userId, CancellationToken ct)
    {
        long deleted = 0;
        await foreach (var tokenKey in _cache.ScanKeysAsync(FormCacheKeys.ResponseShareTokenPrefix + "*", ct))
        {
            var entry = await _cache.TryGetAsync<ShareCacheEntry>(tokenKey, ct);
            if (entry is null || entry.SharedByUserId != userId) continue;

            var token = tokenKey[FormCacheKeys.ResponseShareTokenPrefix.Length..];
            var responseIds = (entry.InstanceResponseIds ?? []).Append(entry.ResponseId).Distinct();
            foreach (var responseId in responseIds)
            {
                var responseKey = FormCacheKeys.ResponseShareResponse(responseId);
                if (await _cache.TryGetAsync<string>(responseKey, ct) == token)
                    await _cache.RemoveAsync(responseKey, ct);
            }

            await _cache.RemoveAsync(tokenKey, ct);
            deleted++;
        }

        return deleted;
    }
}
