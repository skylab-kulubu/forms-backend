using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Tests.Domain.Workflows;

/// <summary>Testlerin yalnız anlamlı alanları yazarak akış grafı kurması için.</summary>
internal sealed class GraphBuilder
{
    public List<FormWorkflowNode> Nodes { get; } = [];
    public List<FormWorkflowTransition> Transitions { get; } = [];
    public Dictionary<Guid, WorkflowNodeForm> Forms { get; } = [];

    public FormWorkflowNode Node(string key, bool start = false, bool review = false, bool timed = false, string[]? questions = null)
    {
        var node = new FormWorkflowNode { NodeKey = key, FormId = Guid.NewGuid(), IsStart = start, RequiresManualReview = review };

        Nodes.Add(node);
        Forms[node.FormId] = new WorkflowNodeForm(true, true, false, true, questions ?? [], timed);

        return node;
    }

    public FormWorkflowTransition Route(
        FormWorkflowNode from,
        FormWorkflowNode? to,
        WorkflowTransitionTrigger trigger = WorkflowTransitionTrigger.ResponseSubmitted,
        int priority = 0,
        WorkflowConditionGroup? condition = null)
    {
        var transition = new FormWorkflowTransition
        {
            SourceNodeId = from.Id,
            TargetNodeId = to?.Id,
            Trigger = trigger,
            Priority = priority,
            Condition = condition
        };

        Transitions.Add(transition);

        return transition;
    }

    public static WorkflowConditionGroup When(string questionId, WorkflowConditionComparison comparison, string? value = null, string? nodeKey = null) =>
        new() { Rules = [new WorkflowConditionRule { QuestionId = questionId, Comparison = comparison, Value = value, NodeKey = nodeKey }] };

    public string[] Codes() => [.. WorkflowGraphValidator.Validate(Nodes, Transitions, Forms).Select(error => error.Code)];
}
