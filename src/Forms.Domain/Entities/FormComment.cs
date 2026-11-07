namespace Skylab.Forms.Domain.Entities;

public class FormComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormId { get; set; }
    public string FieldId { get; set; } = string.Empty;
    public Guid? ThreadId { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public Form Form { get; set; } = null!;
}
