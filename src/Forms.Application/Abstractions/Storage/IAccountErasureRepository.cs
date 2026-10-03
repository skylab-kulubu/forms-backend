using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IAccountErasureRepository
{
    Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Transaction sonuna kadar request_id üzerinde advisory lock tutar.</summary>
    Task LockAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Kişinin verisini siler; anahtar başına değişen kayıt sayısını döner.</summary>
    Task<Dictionary<string, long>> EraseAsync(AccountErasureCommand command, CancellationToken ct = default);

    Task AddReceiptAsync(AccountErasureReceipt receipt, CancellationToken ct = default);
}
