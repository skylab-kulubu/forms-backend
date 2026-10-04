using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

public class FormAttemptEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public FormAttempt Attempt { get; set; } = null!;
    public FormAttemptEventType Type { get; set; }
    public Guid? ActorUserId { get; set; }
    public int? Minutes { get; set; }
    public string? Note { get; set; }
    public DateTime? DeadlineAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
