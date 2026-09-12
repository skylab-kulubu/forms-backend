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

    /// <summary>
    /// null veya kuralsız ise bu, grubun varsayılan rotasıdır: hiçbir koşul
    /// tutmadığında izlenir. Varsayılanlık ayrı bir bayrakla değil koşulun
    /// yokluğuyla belirlenir ki ikisi birbiriyle çelişemesin.
    /// </summary>
    public WorkflowConditionGroup? Condition { get; set; }

    /// <summary>Küçük değer önce değerlendirilir; varsayılan rota her hâlükârda en sona kalır.</summary>
    public int Priority { get; set; }

    public bool IsDefaultRoute => Condition is null || Condition.Rules.Count == 0;
}
