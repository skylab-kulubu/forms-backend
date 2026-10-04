namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Yanıtın hangi paylaşımdan geldiği: form sayfası açılırken adreste duran utm_* etiketleri.
/// Etiketsiz gelen yanıtta hiç kayıt olmaz.
/// </summary>
public class ResponseAttribution
{
    public string? Source { get; set; }
    public string? Medium { get; set; }
    public string? Campaign { get; set; }
    public string? Term { get; set; }
    public string? Content { get; set; }
}
