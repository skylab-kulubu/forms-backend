using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

/// <summary>
/// Tek bir kullanıcının akışı bir kez çalıştırması. Seçilen rota adımlara
/// yazıldığı için sonradan yeniden hesaplanmaz.
/// </summary>
public class FormWorkflowInstance : BaseEntity
{
    public Guid WorkflowId { get; set; }
    public FormWorkflow Workflow { get; set; } = null!;

    public Guid WorkflowVersionId { get; set; }
    public FormWorkflowVersion WorkflowVersion { get; set; } = null!;

    public Guid UserId { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Active;
    public WorkflowInstanceOutcome Outcome { get; set; } = WorkflowInstanceOutcome.None;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public ICollection<FormWorkflowStep> Steps { get; set; } = new List<FormWorkflowStep>();
}
