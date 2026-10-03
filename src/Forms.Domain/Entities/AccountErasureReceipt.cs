namespace Skylab.Forms.Domain.Entities;

/// <summary>
/// Core'un bir hesap silme komutunun Forms'ta tamamlandığının kanıtı. Kişinin
/// kimliğini ya da adresini tutmaz; aynı komut tekrar gelirse iş yeniden yapılmaz,
/// kayıtlı cevap aynen döner. Hiçbir kod yolu bu kaydı silmez.
/// </summary>
public class AccountErasureReceipt
{
    public Guid RequestId { get; set; }
    public DateTime CompletedAt { get; set; }

    /// <summary>Adım başına değişen satır sayısı, snake_case anahtarlı JSON nesnesi.</summary>
    public string Counts { get; set; } = "{}";
}
