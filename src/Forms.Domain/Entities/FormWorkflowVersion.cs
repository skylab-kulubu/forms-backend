using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

/// <summary>
/// Akış tanımının donmuş bir kopyası. Yayınlanmış bir version değiştirilmez;
/// düzenleme yeni bir draft version üretir, böylece devam eden başvurular
/// başladıkları tanıma bağlı kalır.
/// </summary>
public class FormWorkflowVersion : BaseEntity
{
    public Guid WorkflowId { get; set; }
    public FormWorkflow Workflow { get; set; } = null!;

    public int Version { get; set; } = 1;
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Draft;
    public DateTime? PublishedAt { get; set; }

    public ICollection<FormWorkflowNode> Nodes { get; set; } = new List<FormWorkflowNode>();
    public ICollection<FormWorkflowTransition> Transitions { get; set; } = new List<FormWorkflowTransition>();
}
