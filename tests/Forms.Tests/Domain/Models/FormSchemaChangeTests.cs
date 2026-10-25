using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Domain.Models;

public class FormSchemaChangeTests
{
    private static FormSchemaItem Question(string id, string type, params (string Key, object Value)[] props)
    {
        var item = new FormSchemaItem { Id = id, Type = type };
        foreach (var (key, value) in props) item.Props[key] = value;

        return item;
    }

    [Fact]
    public void The_order_of_property_keys_does_not_change_a_schema()
    {
        List<FormSchemaItem> stored = [Question("q", "short_text", ("question", "Adın?"), ("required", true))];
        List<FormSchemaItem> sent = [Question("q", "short_text", ("required", true), ("question", "Adın?"))];

        Assert.True(FormSchemaChange.IsSame(stored, sent));
    }

    [Fact]
    public void A_new_label_changes_the_schema_but_keeps_the_drafts()
    {
        List<FormSchemaItem> before = [Question("q", "short_text", ("question", "Adın?"))];
        List<FormSchemaItem> after = [Question("q", "short_text", ("question", "Adın nedir?"))];

        Assert.False(FormSchemaChange.IsSame(before, after));
        Assert.False(FormSchemaChange.BreaksDrafts(before, after));
    }

    [Fact]
    public void Only_a_question_whose_type_changes_breaks_the_drafts()
    {
        List<FormSchemaItem> before = [Question("kept", "short_text"), Question("retyped", "short_text"), Question("removed", "file")];

        Assert.False(FormSchemaChange.BreaksDrafts(before, [Question("kept", "short_text"), Question("retyped", "short_text"), Question("added", "file")]));
        Assert.True(FormSchemaChange.BreaksDrafts(before, [Question("kept", "short_text"), Question("retyped", "long_text")]));
    }

    [Fact]
    public void A_question_without_an_id_does_not_stop_the_comparison()
    {
        List<FormSchemaItem> schema = [new FormSchemaItem { Id = null!, Type = "short_text" }];

        Assert.False(FormSchemaChange.BreaksDrafts(schema, schema));
    }
}
