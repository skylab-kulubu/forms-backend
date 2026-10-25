using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Domain.Workflows;
using static Skylab.Forms.Tests.Domain.Workflows.GraphBuilder;

namespace Skylab.Forms.Tests.Domain.Workflows;

public class WorkflowTransitionResolverTests
{
    private static readonly Guid Source = Guid.NewGuid();

    private static readonly WorkflowEvaluationContext Context = new("start", new Dictionary<string, IReadOnlyList<FormResponseSchemaItem>>
    {
        ["start"] = [new FormResponseSchemaItem { Id = "q", Answer = "evet" }]
    });

    private static FormWorkflowTransition Route(
        int priority,
        string? whenAnswerIs = null,
        WorkflowTransitionTrigger trigger = WorkflowTransitionTrigger.ResponseSubmitted,
        Guid? source = null) => new()
    {
        SourceNodeId = source ?? Source,
        TargetNodeId = Guid.NewGuid(),
        Trigger = trigger,
        Priority = priority,
        Condition = whenAnswerIs is null ? null : When("q", WorkflowConditionComparison.Equals, whenAnswerIs)
    };

    private static WorkflowRouteDecision Resolve(params FormWorkflowTransition[] transitions) =>
        WorkflowTransitionResolver.Resolve(transitions, Source, WorkflowTransitionTrigger.ResponseSubmitted, Context);

    [Fact]
    public void Without_a_route_for_the_trigger_the_flow_ends() =>
        Assert.Equal(WorkflowRouteOutcome.Terminal, Resolve(Route(0, trigger: WorkflowTransitionTrigger.ResponseApproved)).Outcome);

    [Fact]
    public void Routes_of_other_steps_are_ignored() =>
        Assert.Equal(WorkflowRouteOutcome.Terminal, Resolve(Route(0, source: Guid.NewGuid())).Outcome);

    [Fact]
    public void The_default_route_is_tried_last_whatever_its_priority()
    {
        var fallback = Route(0);
        var match = Route(5, whenAnswerIs: "evet");

        Assert.Same(match, Resolve(fallback, match).Transition);
    }

    [Fact]
    public void Conditions_are_tried_in_priority_order()
    {
        var first = Route(1, whenAnswerIs: "evet");
        var second = Route(2, whenAnswerIs: "evet");

        Assert.Same(first, Resolve(second, first).Transition);
    }

    [Fact]
    public void When_no_condition_matches_the_default_route_is_followed()
    {
        var fallback = Route(9);

        Assert.Same(fallback, Resolve(Route(0, whenAnswerIs: "hayır"), fallback).Transition);
    }

    [Fact]
    public void Without_a_match_or_a_default_the_decision_is_unresolved() =>
        Assert.Equal(WorkflowRouteOutcome.Unresolved, Resolve(Route(0, whenAnswerIs: "hayır")).Outcome);
}
