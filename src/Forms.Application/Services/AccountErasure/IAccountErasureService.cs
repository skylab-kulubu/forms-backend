using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.AccountErasure;

public interface IAccountErasureService
{
    Task<AccountErasureReceipt?> FindReceiptAsync(Guid requestId, CancellationToken ct = default);
    Task<AccountErasureReceipt> EraseAsync(AccountErasureCommand command, CancellationToken ct = default);
}
