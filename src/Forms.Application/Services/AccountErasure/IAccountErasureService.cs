using Skylab.Forms.Application.Contracts.AccountErasure;

namespace Skylab.Forms.Application.Services.AccountErasure;

/// <summary>
/// Core'un hesap silme komutunu Forms'ta yürütür (ADR-0051). Kayıtlar kalır, kişi
/// gider: kişinin yanıtları boşalır, aktör kolonları Silinmiş kullanıcı olur,
/// ilişki satırları ve geçici veri silinir.
/// </summary>
public interface IAccountErasureService
{
    /// <summary>Komut daha önce tamamlandıysa makbuzu.</summary>
    Task<AccountErasureReceiptContract?> FindCompletedAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Silmeyi yapar ve makbuzu döner. Kişinin hesap erişim kapısında engellenmiş
    /// olduğunu çağıran doğrular.
    /// </summary>
    Task<AccountErasureReceiptContract> EraseAsync(AccountErasureCommand command, CancellationToken ct = default);
}
