using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Tests.Domain.Workflows;

public class WorkflowConditionEvaluatorTests
{
    private static WorkflowEvaluationContext Answers(params (string QuestionId, string? Answer)[] answers) =>
        new("current", new Dictionary<string, IReadOnlyList<FormResponseSchemaItem>>
        {
            ["current"] = [.. answers.Select(answer => new FormResponseSchemaItem { Id = answer.QuestionId, Answer = answer.Answer })]
        });

    private static bool Matches(WorkflowConditionComparison comparison, string? answer, string? value = null, List<string>? values = null) =>
        WorkflowConditionEvaluator.Matches(
            new WorkflowConditionGroup { Rules = [new WorkflowConditionRule { QuestionId = "q", Comparison = comparison, Value = value, Values = values }] },
            Answers(("q", answer)));

    [Fact]
    public void A_missing_or_empty_condition_always_matches()
    {
        Assert.True(WorkflowConditionEvaluator.Matches(null, Answers()));
        Assert.True(WorkflowConditionEvaluator.Matches(new WorkflowConditionGroup(), Answers()));
    }

    [Theory]
    [InlineData("  Evet ", "evet", true)]
    [InlineData("ISTANBUL", "istanbul", true)]
    [InlineData("İstanbul", "istanbul", false)]
    [InlineData("Hayır", "Evet", false)]
    [InlineData(null, "Evet", false)]
    public void Equals_compares_trimmed_text_ignoring_case_ordinally(string? answer, string value, bool expected) =>
        Assert.Equal(expected, Matches(WorkflowConditionComparison.Equals, answer, value));

    [Theory]
    [InlineData("Ankara, Türkiye", true)]
    [InlineData("[\"İzmir\",\"Ankara, Türkiye\"]", true)]
    [InlineData("İzmir, Bursa", false)]
    public void In_tries_the_whole_answer_before_its_selections(string answer, bool expected) =>
        Assert.Equal(expected, Matches(WorkflowConditionComparison.In, answer, values: ["Ankara, Türkiye"]));

    [Theory]
    [InlineData("   ", true)]
    [InlineData(null, true)]
    [InlineData("x", false)]
    public void IsEmpty_treats_whitespace_as_empty(string? answer, bool expected) =>
        Assert.Equal(expected, Matches(WorkflowConditionComparison.IsEmpty, answer));

    [Fact]
    public void Contains_with_a_blank_value_never_matches() =>
        Assert.False(Matches(WorkflowConditionComparison.Contains, "her şey", " "));

    [Theory]
    [InlineData("3,5", WorkflowConditionComparison.GreaterThan, "3", true)]
    [InlineData("3.5", WorkflowConditionComparison.LessThanOrEqual, "3,5", true)]
    [InlineData("1,000.5", WorkflowConditionComparison.GreaterThan, "1", false)]
    [InlineData("on", WorkflowConditionComparison.GreaterThan, "1", false)]
    [InlineData("", WorkflowConditionComparison.LessThan, "1", false)]
    public void Numbers_accept_a_single_decimal_comma_and_unreadable_ones_fail_the_rule(
        string answer, WorkflowConditionComparison comparison, string value, bool expected) =>
        Assert.Equal(expected, Matches(comparison, answer, value));

    [Fact]
    public void Any_needs_one_rule_and_All_needs_every_rule()
    {
        List<WorkflowConditionRule> rules =
        [
            new() { QuestionId = "a", Comparison = WorkflowConditionComparison.Equals, Value = "1" },
            new() { QuestionId = "b", Comparison = WorkflowConditionComparison.Equals, Value = "2" }
        ];
        var context = Answers(("a", "1"), ("b", "3"));

        Assert.True(WorkflowConditionEvaluator.Matches(new WorkflowConditionGroup { Operator = WorkflowConditionOperator.Any, Rules = rules }, context));
        Assert.False(WorkflowConditionEvaluator.Matches(new WorkflowConditionGroup { Operator = WorkflowConditionOperator.All, Rules = rules }, context));
    }

    [Fact]
    public void A_rule_with_a_node_key_reads_that_steps_answers()
    {
        var context = new WorkflowEvaluationContext("second", new Dictionary<string, IReadOnlyList<FormResponseSchemaItem>>
        {
            ["first"] = [new FormResponseSchemaItem { Id = "q", Answer = "evet" }],
            ["second"] = [new FormResponseSchemaItem { Id = "q", Answer = "hayır" }]
        });
        var condition = new WorkflowConditionGroup
        {
            Rules = [new WorkflowConditionRule { NodeKey = "first", QuestionId = "q", Comparison = WorkflowConditionComparison.Equals, Value = "evet" }]
        };

        Assert.True(WorkflowConditionEvaluator.Matches(condition, context));
    }
}
