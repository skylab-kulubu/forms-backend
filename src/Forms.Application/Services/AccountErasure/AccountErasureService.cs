using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Application.Services.AccountErasure;

public class AccountErasureService : IAccountErasureService
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

    public async Task<Dictionary<string, long>> EraseAsync(AccountErasureCommand command, CancellationToken ct = default)
    {
        var userId = command.SubjectId;
        var emails = command.Emails;
        var hasEmails = emails.Count > 0;

        // Redis transaction'a girmez, bu yüzden taslaklar önce silinir. Yanıt paylaşım bağlantıları en çok bir
        // saat yaşadığı için aranmaz (sözleşme §8 istisnası).
        var drafts = await DeleteDraftsAsync(userId, ct);

        // İş doğası gereği idempotenttir: tekrarı silecek bir şey bulamaz ve her şeyi 0 sayar, bu yüzden makbuz
        // ve kilit yoktur (sözleşme §10). Misafir kaydı hesaba bağlı değildir ve adresi doğrulanmaz; kimin
        // yazdığı bilinmediği için kayda dokunulmaz, yalnız kişinin doğrulanmış adresleri çıkarılır.
        var counts = await _uow.ExecuteInTransactionAsync(async token => new Dictionary<string, long>
        {
            ["responses_redacted"] = await _erasures.RedactResponsesAsync(userId, DeletedUser.Id, token),
            ["attempts_deleted"] = await _erasures.DeleteAttemptsAsync(userId, token),
            ["answer_rows_cleared"] = hasEmails ? await _erasures.ClearAnswerMentionsAsync(emails, token) : 0,
            ["review_notes_cleared"] = hasEmails ? await _erasures.ClearNoteMentionsAsync(emails, token) : 0,
            ["guest_emails_cleared"] = hasEmails ? await _erasures.ClearGuestEmailsAsync(emails, token) : 0,
            ["actor_columns_replaced"] = await _erasures.ReplaceActorColumnsAsync(userId, DeletedUser.Id, token),
            // Owner satırı silinmez, Silinmiş kullanıcıya geçer: her formun tek sahibi kalır, kimse kendiliğinden
            // sahip olmaz.
            ["collaborators_deleted"] = await _erasures.RemoveCollaboratorsAsync(userId, token),
            ["form_owners_replaced"] = await _erasures.ReplaceOwnersAsync(userId, DeletedUser.Id, token),
            ["workflow_runs_cancelled"] = await _erasures.CloseWorkflowRunsAsync(userId, DateTime.UtcNow, token),
            ["workflow_runs_detached"] = await _erasures.DetachWorkflowRunsAsync(userId, DeletedUser.Id, token),
            ["answer_file_links_deleted"] = await _erasures.DeleteAnswerFileLinksAsync(userId, token),
            ["notifications_cleared"] = await _erasures.ClearNotificationsAsync(userId, emails, token),
            ["drafts_deleted"] = drafts
        }, ct);

        // Önbellekteki form analizleri silinen cevapları içerebilir; sonraki okumada yeniden hesaplanır.
        await _cache.TryRemoveByPrefixAsync(FormCacheKeys.AnalyticsPrefix, ct);

        return counts;
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
}
