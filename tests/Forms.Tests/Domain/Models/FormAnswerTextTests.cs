using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Domain.Models;

public class FormAnswerTextTests
{
    [Theory]
    [InlineData("[\"A\", \" B \", \"\"]", new[] { "A", "B" })]
    [InlineData("A, B", new[] { "A", "B" })]
    [InlineData("[bozuk", new[] { "[bozuk" })]
    [InlineData("  ", new string[0])]
    public void Selections_read_json_arrays_and_comma_lists(string answer, string[] expected) =>
        Assert.Equal(expected, FormAnswerText.Selections(answer));

    [Theory]
    [InlineData("[\"A\",\"B\"]", "A, B")]
    [InlineData("Ali, Veli", "Ali, Veli")]
    [InlineData(null, "")]
    public void Only_json_arrays_are_reformatted_for_display(string? answer, string expected) =>
        Assert.Equal(expected, FormAnswerText.ToDisplayText(answer));
}
