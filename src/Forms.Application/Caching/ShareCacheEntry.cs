namespace Skylab.Forms.Application.Caching;

/// <param name="InstanceResponseIds">
/// Paylasim, cevabin ait oldugu basvurunun butun adimlarini kapsar: inceleyen
/// baslangictan itibaren tum cevaplari gorebilsin.
/// </param>
public record ShareCacheEntry(Guid ResponseId, List<Guid> InstanceResponseIds, Guid SharedByUserId);
