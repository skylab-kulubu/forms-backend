using Skylab.Forms.Application.Contracts.AccountErasure;

namespace Skylab.Forms.Application.Services.AccountErasure;

public interface IAccountErasureService
{
    /// <summary>Kişinin verisini siler; anahtar başına değişen kayıt sayısını döner.</summary>
    Task<Dictionary<string, long>> EraseAsync(AccountErasureCommand command, CancellationToken ct = default);
}
