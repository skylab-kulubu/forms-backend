using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Workflows;
using static Skylab.Forms.Tests.Domain.Workflows.GraphBuilder;

namespace Skylab.Forms.Tests.Domain.Workflows;

public class WorkflowGraphValidatorTests
{
    [Fact]
    public void A_linear_flow_with_a_reviewed_step_is_valid()
    {
        var graph = new GraphBuilder();
        var apply = graph.Node("apply", start: true);
        var interview = graph.Node("interview", review: true);
        graph.Route(apply, interview);
        graph.Route(interview, null, WorkflowTransitionTrigger.ResponseApproved);
        graph.Route(interview, null, WorkflowTransitionTrigger.ResponseDeclined);

        Assert.Empty(graph.Codes());
    }

    [Fact]
    public void An_empty_graph_is_rejected() =>
        Assert.Equal(new[] { "emptyGraph" }, new GraphBuilder().Codes());

    [Fact]
    public void Exactly_one_start_step_is_required()
    {
        var none = new GraphBuilder();
        none.Node("a");

        var two = new GraphBuilder();
        two.Node("a", start: true);
        two.Node("b", start: true);

        Assert.Contains("startNodeMissing", none.Codes());
        Assert.Contains("startNodeAmbiguous", two.Codes());
    }

    [Fact]
    public void Step_keys_are_unique_ignoring_case()
    {
        var graph = new GraphBuilder();
        graph.Node("Apply", start: true);
        graph.Node("apply");

        Assert.Contains("nodeKeyDuplicated", graph.Codes());
    }

    [Fact]
    public void A_cycle_is_rejected()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true);
        var b = graph.Node("b");
        graph.Route(a, b);
        graph.Route(b, a);

        Assert.Equal(new[] { "cycleDetected" }, graph.Codes());
    }

    [Fact]
    public void A_step_the_start_cannot_reach_is_reported()
    {
        var graph = new GraphBuilder();
        graph.Node("a", start: true);
        graph.Node("orphan");

        Assert.Equal(new[] { "nodeUnreachable" }, graph.Codes());
    }

    [Fact]
    public void A_route_may_hold_at_most_three_forms()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true);
        var b = graph.Node("b");
        var c = graph.Node("c");
        var d = graph.Node("d");
        graph.Route(a, b);
        graph.Route(b, c);
        graph.Route(c, d);

        var errors = WorkflowGraphValidator.Validate(graph.Nodes, graph.Transitions, graph.Forms);

        var error = Assert.Single(errors);
        Assert.Equal(("depthExceeded", "d"), (error.Code, error.NodeKey));
    }

    [Fact]
    public void A_reviewed_step_routes_only_after_the_decision()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true, review: true);
        graph.Route(a, null);

        Assert.Equal(new[] { "triggerNotAllowed" }, graph.Codes());
    }

    [Fact]
    public void A_step_without_review_routes_only_on_submit()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true);
        graph.Route(a, null, WorkflowTransitionTrigger.ResponseApproved);

        Assert.Equal(new[] { "triggerNotAllowed" }, graph.Codes());
    }

    [Theory]
    [InlineData(false, new[] { "timeoutNotAllowed" })]
    [InlineData(true, new string[0])]
    public void A_timeout_route_needs_a_timed_form(bool timed, string[] expected)
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true, timed: timed);
        graph.Route(a, null);
        graph.Route(a, null, WorkflowTransitionTrigger.TimedOut);

        Assert.Equal(expected, graph.Codes());
    }

    [Fact]
    public void Conditional_routes_need_a_default_route()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true, questions: ["q"]);
        var b = graph.Node("b");
        graph.Route(a, b, condition: When("q", WorkflowConditionComparison.Equals, "evet"));

        Assert.Equal(new[] { "defaultRouteMissing" }, graph.Codes());
    }

    [Fact]
    public void Two_routes_of_a_trigger_cannot_share_a_priority()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true, questions: ["q"]);
        var b = graph.Node("b");
        var c = graph.Node("c");
        graph.Route(a, b, priority: 0, condition: When("q", WorkflowConditionComparison.Equals, "evet"));
        graph.Route(a, c, priority: 0);

        Assert.Equal(new[] { "priorityDuplicated" }, graph.Codes());
    }

    [Fact]
    public void A_condition_may_only_read_a_step_that_lies_on_every_route()
    {
        // a'dan b ya da c üzerinden d'ye gidilir; d'nin koşulu b'yi okursa c'den gelen başvuruda o cevap yoktur.
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true, questions: ["q"]);
        var b = graph.Node("b", questions: ["q"]);
        var c = graph.Node("c");
        var d = graph.Node("d");
        graph.Route(a, b, priority: 0, condition: When("q", WorkflowConditionComparison.Equals, "evet"));
        graph.Route(a, c, priority: 1);
        graph.Route(b, d);
        graph.Route(c, d);
        graph.Route(d, null, priority: 0, condition: When("q", WorkflowConditionComparison.IsEmpty, nodeKey: "b"));
        graph.Route(d, null, priority: 1);

        Assert.Equal(new[] { "conditionNodeNotGuaranteed" }, graph.Codes());
    }

    [Fact]
    public void A_condition_names_a_question_of_the_form_and_carries_a_value()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true, questions: ["q"]);
        var b = graph.Node("b");
        var condition = When("missing", WorkflowConditionComparison.Equals, "evet");
        condition.Rules.Add(new() { QuestionId = "q", Comparison = WorkflowConditionComparison.Equals });
        graph.Route(a, b, priority: 0, condition: condition);
        graph.Route(a, b, priority: 1);

        Assert.Equal(new[] { "conditionQuestionMissing", "conditionValueMissing" }, graph.Codes());
    }

    [Fact]
    public void Each_step_form_must_be_open_owned_and_for_signed_in_people()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true);
        graph.Forms[a.FormId] = new WorkflowNodeForm(Exists: true, IsOpen: false, AllowAnonymousResponses: true, WorkflowOwnerIsFormOwner: false, QuestionIds: []);

        Assert.Equal(new[] { "formClosed", "formAnonymous", "formNotOwned" }, graph.Codes());
    }

    [Fact]
    public void A_deleted_form_is_reported()
    {
        var graph = new GraphBuilder();
        var a = graph.Node("a", start: true);
        graph.Forms.Remove(a.FormId);

        Assert.Equal(new[] { "formMissing" }, graph.Codes());
    }
}
