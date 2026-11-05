namespace Skylab.Forms.Domain.Entities;

public class FormEdit
{
    public Guid FormId { get; set; }
    public long Version { get; set; }
    public Guid BatchId { get; set; }
    public Guid UserId { get; set; }
    public string Ops { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public Form Form { get; set; } = null!;
}
