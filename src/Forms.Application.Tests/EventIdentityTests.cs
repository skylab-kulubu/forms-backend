using Skylab.Forms.Application;
using Skylab.Forms.Domain.Models;
using Xunit;

namespace Forms.Application.Tests;

public class EventIdentityTests
{
    [Fact]
    public void Ensure_injects_locked_ad_soyad_email()
    {
        var schema = EventIdentity.Ensure([]);
        Assert.Equal(3, schema.Count);
        Assert.Equal("firstName", schema[0].Props["identity"]);
        Assert.Equal("lastName", schema[1].Props["identity"]);
        Assert.Equal("email", schema[2].Props["identity"]);
        Assert.All(schema, field => Assert.Equal(true, field.Props["required"]));
        Assert.Equal("Ad", schema[0].Props["question"]);
        Assert.Equal("Soyad", schema[1].Props["question"]);
        Assert.Equal("E-posta", schema[2].Props["question"]);
    }

    [Fact]
    public void Ensure_locks_existing_questions_instead_of_duplicating()
    {
        var schema = EventIdentity.Ensure(
        [
            new FormSchemaItem
            {
                Id = "q-ad",
                Type = "short_text",
                Props = new Dictionary<string, object?> { ["question"] = "Adınız", ["required"] = false, ["inputType"] = "text" }
            },
            new FormSchemaItem
            {
                Id = "q-extra",
                Type = "long_text",
                Props = new Dictionary<string, object?> { ["question"] = "Neden?", ["required"] = false }
            }
        ]);

        Assert.Equal("q-ad", schema[0].Id);
        Assert.Equal("firstName", schema[0].Props["identity"]);
        Assert.Equal(true, schema[0].Props["required"]);
        Assert.Contains(schema, field => field.Props["identity"] is "lastName");
        Assert.Contains(schema, field => field.Props["identity"] is "email");
        Assert.Equal("q-extra", schema[^1].Id);
        Assert.Equal(4, schema.Count);
    }

    [Fact]
    public void Extract_requires_ad_soyad_email()
    {
        var schema = EventIdentity.Ensure([]);
        Assert.Null(EventIdentity.Extract(schema, []));
        Assert.Null(EventIdentity.Extract(schema,
        [
            new FormResponseSchemaItem { Id = schema[0].Id, Answer = "Ada" },
            new FormResponseSchemaItem { Id = schema[1].Id, Answer = "Lovelace" },
            new FormResponseSchemaItem { Id = schema[2].Id, Answer = "not-an-email" }
        ]));

        var guest = EventIdentity.Extract(schema,
        [
            new FormResponseSchemaItem { Id = schema[0].Id, Answer = "Ada" },
            new FormResponseSchemaItem { Id = schema[1].Id, Answer = "Lovelace" },
            new FormResponseSchemaItem { Id = schema[2].Id, Answer = "ada@example.com" }
        ]);
        Assert.NotNull(guest);
        Assert.Equal("Ada", guest!.FirstName);
        Assert.Equal("Lovelace", guest.LastName);
        Assert.Equal("ada@example.com", guest.Email);
    }

    [Fact]
    public void Ensure_does_not_mutate_source()
    {
        var source = new List<FormSchemaItem>
        {
            new()
            {
                Id = "old",
                Type = "short_text",
                Props = new Dictionary<string, object?> { ["question"] = "Ad", ["required"] = false }
            }
        };
        EventIdentity.Ensure(source);
        Assert.False(source[0].Props.ContainsKey("identity"));
        Assert.Equal(false, source[0].Props["required"]);
    }
}
