using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Domain.Entities;

public class FormWorkflowTransition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkflowVersionId { get; set; }
    public FormWorkflowVersion WorkflowVersion { get; set; } = null!;

    public Guid SourceNodeId { get; set; }
    public FormWorkflowNode SourceNode { get; set; } = null!;

    /// <summary>null ise akış bu tetikte tamamlanır.</summary>
    public Guid? TargetNodeId { get; set; }
    public FormWorkflowNode? TargetNode { get; set; }

    public WorkflowTransitionTrigger Trigger { get; set; }

    /// <summary>null ise koşulsuz eşleşir; yalnızca fallback transition'larda beklenir.</summary>
    public WorkflowConditionGroup? Condition { get; set; }

    /// <summary>Küçük değer önce değerlendirilir.</summary>
    public int Priority { get; set; }

    /// <summary>Hiçbir koşul tutmadığında seçilen transition.</summary>
    public bool IsFallback { get; set; } = false;
}
