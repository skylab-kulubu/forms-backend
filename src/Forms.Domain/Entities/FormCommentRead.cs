namespace Skylab.Forms.Domain.Entities;

public class FormCommentRead
{
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadUntil { get; set; }
    public FormComment Thread { get; set; } = null!;
}
