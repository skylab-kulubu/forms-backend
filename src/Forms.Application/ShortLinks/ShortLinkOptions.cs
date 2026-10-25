namespace Skylab.Forms.Application.ShortLinks;

public class ShortLinkOptions
{
    public const string SectionName = "ShortLinks";

    /// <summary>Formun herkese açık adresinin kökü; kısa link buraya yönlenir. Kısa link sağlayıcısı açıkken zorunludur.</summary>
    public string FormsPublicUrl { get; set; } = "";
}
