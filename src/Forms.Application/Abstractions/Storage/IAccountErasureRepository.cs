using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IAccountErasureRepository
{
    Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default);

    Task LockAsync(Guid requestId, CancellationToken ct = default);

    Task<Dictionary<string, long>> EraseAsync(AccountErasureCommand command, CancellationToken ct = default);

    Task AddReceiptAsync(AccountErasureReceipt receipt, CancellationToken ct = default);
}
