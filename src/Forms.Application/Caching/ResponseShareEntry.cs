namespace Skylab.Forms.Application.Caching;

/// <summary>Bir yanıt paylaşım bağlantısının Redis'teki kaydı.</summary>
/// <param name="InstanceResponseIds">
/// Paylasim, cevabin ait oldugu basvurunun butun adimlarini kapsar: inceleyen
/// baslangictan itibaren tum cevaplari gorebilsin.
/// </param>
public record ResponseShareEntry(Guid ResponseId, List<Guid> InstanceResponseIds, Guid SharedByUserId);
