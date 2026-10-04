using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Domain.Entities;

public class FormAttempt : BaseEntity
{
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public Guid UserId { get; set; }
    public Guid? WorkflowStepId { get; set; }
    public FormAttemptStatus Status { get; set; } = FormAttemptStatus.Opened;
    public DateTime? StartedAt { get; set; }
    public DateTime? DeadlineAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public Guid? ResponseId { get; set; }
    public List<FormResponseSchemaItem>? DraftSnapshot { get; set; }
    public DateTime? ReminderSentAt { get; set; }

    public ICollection<FormAttemptEvent> Events { get; set; } = new List<FormAttemptEvent>();

    public bool IsRunningAt(DateTime now) =>
        Status == FormAttemptStatus.Started && DeadlineAt is { } deadline && now < deadline;

    public bool AcceptsSubmissionAt(DateTime now, TimeSpan grace) =>
        Status == FormAttemptStatus.Started && DeadlineAt is { } deadline && now <= deadline + grace;
}
