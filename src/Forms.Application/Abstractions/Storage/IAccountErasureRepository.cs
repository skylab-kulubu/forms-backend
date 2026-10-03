using Skylab.Forms.Application.Contracts.AccountErasure;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IAccountErasureRepository
{
    Task<AccountErasureReceiptContract?> FindReceiptAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Kişinin kendi yanıtları ve adresleriyle eşleşen misafir yanıtları, silinmiş
    /// formlarınkiler dahil.
    /// </summary>
    Task<IReadOnlyList<AccountErasureSubjectResponse>> GetSubjectResponsesAsync(
        AccountErasureCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Silmeyi ve makbuzu tek transaction'da yazar. Transaction komutun kimliği
    /// üzerinde kilit tutar; makbuz zaten varsa hiçbir şey değiştirmeden onu döner.
    /// </summary>
    Task<AccountErasureReceiptContract> EraseAsync(AccountErasureWork work, CancellationToken ct = default);
}
