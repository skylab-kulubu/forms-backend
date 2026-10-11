namespace Skylab.Forms.Application.Abstractions.Storage;

/// <summary>
/// Hesap silmenin veritabanı tarafı. Sorgu filtreleri kapalıdır: silinmiş formlar ve arşivli şablonlar da
/// dahil. Her toplu işlem değiştirdiği satır sayısını döner.
/// </summary>
public interface IAccountErasureRepository
{
    Task<int> RedactResponsesAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> DeleteAttemptsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Adreslerden birini geçiren cevapları boşaltır; değişen yanıt ve deneme satırı sayısını döner.</summary>
    Task<int> ClearAnswerMentionsAsync(IReadOnlyList<string> emails, CancellationToken ct = default);

    /// <summary>Adreslerden birini geçiren inceleme ve deneme notlarını siler.</summary>
    Task<int> ClearNoteMentionsAsync(IReadOnlyList<string> emails, CancellationToken ct = default);

    Task<int> ClearGuestEmailsAsync(IReadOnlyList<string> emails, CancellationToken ct = default);
    Task<int> ReplaceActorColumnsAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> RemoveCollaboratorsAsync(Guid userId, CancellationToken ct = default);
    Task<int> ReplaceOwnersAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> CloseWorkflowRunsAsync(Guid userId, DateTime now, CancellationToken ct = default);
    Task<int> DetachWorkflowRunsAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> DeleteAnswerFileLinksAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Kişinin bekleyen core bildirimlerini siler, misafir bildirimlerinden adreslerini çıkarır.</summary>
    Task<int> ClearNotificationsAsync(Guid userId, IReadOnlyList<string> emails, CancellationToken ct = default);
}
