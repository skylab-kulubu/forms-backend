namespace Skylab.Forms.Domain.Entities;

public class AccountErasureReceipt
{
    public Guid RequestId { get; set; }
    public DateTime CompletedAt { get; set; }
    public string Counts { get; set; } = "{}";
}
