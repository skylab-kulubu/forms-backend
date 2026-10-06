using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

/// <summary>
/// Hesap silmenin veritabanı tarafı. Sorgu filtreleri kapalıdır: silinmiş formlar ve arşivli
/// şablonlar da dahil. Her toplu işlem değiştirdiği satır sayısını döner.
/// </summary>
public interface IAccountErasureRepository
{
    Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Transaction sonuna kadar request_id üzerinde advisory lock tutar.</summary>
    Task LockAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Cevaplarından biri adreslerden birine eşit olan misafir yanıtları (yalnız aday listesi).</summary>
    Task<List<Guid>> FindGuestResponseCandidatesAsync(IReadOnlyList<string> emails, CancellationToken ct = default);

    Task<List<ErasureResponseRow>> GetResponsesAsync(Guid userId, IReadOnlyList<Guid> responseIds, CancellationToken ct = default);

    Task<int> RedactResponsesAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> RedactGuestResponsesAsync(IReadOnlyList<Guid> responseIds, CancellationToken ct = default);
    Task<int> DeleteAttemptsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Başkalarının cevaplarında adresi ya da tam adı geçen cevapları boşaltır; değişen yanıt ve deneme sayısını döner.</summary>
    Task<int> ClearAnswerMentionsAsync(IReadOnlyList<string> emails, IReadOnlyList<string> fullNames, CancellationToken ct = default);

    /// <summary>Adresi ya da tam adı geçen inceleme ve deneme notlarını siler.</summary>
    Task<int> ClearNoteMentionsAsync(IReadOnlyList<string> emails, IReadOnlyList<string> fullNames, CancellationToken ct = default);

    Task<int> ReplaceActorColumnsAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> RemoveCollaboratorsAsync(Guid userId, CancellationToken ct = default);
    Task<int> ReplaceOwnersAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);
    Task<int> CloseWorkflowRunsAsync(Guid userId, DateTime now, CancellationToken ct = default);
    Task<int> DetachWorkflowRunsAsync(Guid userId, Guid replacementUserId, CancellationToken ct = default);

    void Add(AccountErasureReceipt receipt);
}

public sealed record ErasureResponseRow(Guid Id, Guid? UserId, List<FormSchemaItem> Schema, List<FormResponseSchemaItem> Data);
