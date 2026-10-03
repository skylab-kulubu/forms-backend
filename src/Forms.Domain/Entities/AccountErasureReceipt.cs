namespace Skylab.Forms.Domain.Entities;

/// <summary>Core'un bir silme komutunun tamamlandığı kayıt; kişiyi ya da adresi tutmaz.</summary>
public class AccountErasureReceipt
{
    public Guid RequestId { get; set; }
    public DateTime CompletedAt { get; set; }
    public string Counts { get; set; } = "{}";
}
