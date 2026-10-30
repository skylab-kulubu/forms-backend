using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

/// <summary>
/// Tek bir kullanıcının akışı bir kez çalıştırması. Seçilen rota adımlara
/// yazıldığı için sonradan yeniden hesaplanmaz.
/// </summary>
public class FormWorkflowInstance : BaseEntity
{
    /// <summary>WorkflowInstances.VoidNote kolonunun uzunluğu (varchar(500)).</summary>
    public const int VoidNoteMaxLength = 500;

    public Guid WorkflowId { get; set; }
    public FormWorkflow Workflow { get; set; } = null!;

    public Guid WorkflowVersionId { get; set; }
    public FormWorkflowVersion WorkflowVersion { get; set; } = null!;

    public Guid UserId { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Active;
    public WorkflowInstanceOutcome Outcome { get; set; } = WorkflowInstanceOutcome.None;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }

    /// <summary>Başvuruyu geçersiz sayan ekibin adaya notu.</summary>
    public string? VoidNote { get; set; }

    public ICollection<FormWorkflowStep> Steps { get; set; } = new List<FormWorkflowStep>();

    public void Touch(DateTime now) => UpdatedAt = now;

    public void Finish(WorkflowInstanceOutcome outcome, DateTime now)
    {
        Status = outcome is WorkflowInstanceOutcome.Declined or WorkflowInstanceOutcome.TimedOut
            ? WorkflowInstanceStatus.Terminated
            : WorkflowInstanceStatus.Completed;
        Outcome = outcome;
        CompletedAt = now;
    }

    public void Stop(DateTime now, WorkflowInstanceOutcome outcome = WorkflowInstanceOutcome.None)
    {
        Status = WorkflowInstanceStatus.Terminated;
        Outcome = outcome;
        CompletedAt = now;
    }

    public void Fault(DateTime now)
    {
        Status = WorkflowInstanceStatus.Faulted;
        CompletedAt = now;
    }

    public bool Void(Guid actorId, string? note, DateTime now)
    {
        var wasActive = Status == WorkflowInstanceStatus.Active;

        foreach (var step in Steps.Where(step => step.CompletedAt is null))
            step.CompletedAt = now;

        Status = WorkflowInstanceStatus.Voided;
        VoidedAt = now;
        VoidedBy = actorId;
        VoidNote = note;
        if (wasActive) CompletedAt = now;

        return wasActive;
    }
}
