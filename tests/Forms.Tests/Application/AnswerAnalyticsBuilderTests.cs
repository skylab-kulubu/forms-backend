using Skylab.Forms.Application.Services;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Application;

public class AnswerAnalyticsBuilderTests
{
    private static FormResponseSchemaItem Answer(string id, string? answer) => new() { Id = id, Type = "combobox", Answer = answer };

    [Fact]
    public void A_question_counts_as_asked_only_in_the_answers_that_carry_it()
    {
        var form = new Form
        {
            Schema =
            [
                new FormSchemaItem { Id = "team", Type = "combobox" },
                new FormSchemaItem { Id = "stack", Type = "combobox" }
            ]
        };
        List<List<FormResponseSchemaItem>> responses =
        [
            [Answer("team", "Web"), Answer("stack", "React")],
            [Answer("team", "Oyun")],
            [Answer("team", "Web"), Answer("stack", "")]
        ];

        var questions = AnswerAnalyticsBuilder.Build(form, responses, DateTime.UtcNow).Questions;
        var team = questions.Single(question => question.QuestionId == "team");
        var stack = questions.Single(question => question.QuestionId == "stack");

        Assert.Equal((3, 3), (team.AskedCount, team.AnsweredCount));
        Assert.Equal((2, 1), (stack.AskedCount, stack.AnsweredCount));
    }
}
