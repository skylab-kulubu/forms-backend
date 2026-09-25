using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.ShortLinks;

namespace Skylab.Forms.Application.Services.ShortLinks;

public interface IFormShortLinkService
{
    /// <summary>Formun kısa linki; henüz yoksa Data null döner, link oluşturulmaz.</summary>
    Task<ServiceResult<FormShortLinkContract>> GetAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Paylaş penceresi açılınca çağrılır: link varsa getirir, yoksa rastgele adla oluşturur.</summary>
    Task<ServiceResult<FormShortLinkContract>> EnsureAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Boş ad, servisin rastgele bir ad üretmesi demektir.</summary>
    Task<ServiceResult<FormShortLinkContract>> RenameAsync(Guid formId, Guid userId, string? alias, CancellationToken cancellationToken = default);

    Task<ServiceResult<AliasAvailabilityContract>> CheckAliasAsync(Guid formId, Guid userId, string alias, CancellationToken cancellationToken = default);

    /// <summary>
    /// QR görseli core'dan buradan geçer: tarayıcı core'a CORS'suz ulaşamadığı için indirme ve
    /// panoya kopyalama bu uçla çalışır.
    /// </summary>
    Task<ServiceResult<QrImageContract>> GetQrAsync(Guid formId, Guid userId, bool svg, CancellationToken cancellationToken = default);
}
