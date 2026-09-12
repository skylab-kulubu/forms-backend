using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Models;

public class WorkflowConditionGroup
{
    public WorkflowConditionOperator Operator { get; set; } = WorkflowConditionOperator.All;
    public List<WorkflowConditionRule> Rules { get; set; } = new();
}
