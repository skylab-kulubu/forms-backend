using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Domain.Models;

public class ResponseGuestTests
{
    private static FormSchemaItem Field(string id, string? identity = null)
    {
        var field = new FormSchemaItem { Id = id, Type = "text" };
        if (identity is not null) field.Props["identity"] = identity;

        return field;
    }

    private static readonly List<FormSchemaItem> IdentitySchema =
        [Field("f", "firstName"), Field("l", "lastName"), Field("e", "email"), Field("other")];

    private static List<FormResponseSchemaItem> Answers(params (string Id, string Answer)[] answers) =>
        [.. answers.Select(answer => new FormResponseSchemaItem { Id = answer.Id, Answer = answer.Answer })];

    [Fact]
    public void A_form_asks_for_identity_only_with_all_three_fields()
    {
        Assert.True(ResponseGuest.IsAskedBy(IdentitySchema));
        Assert.False(ResponseGuest.IsAskedBy([Field("f", "firstName"), Field("e", "email")]));
    }

    [Fact]
    public void Identity_answers_become_a_guest_with_a_lowercase_address()
    {
        var guest = ResponseGuest.From(IdentitySchema, Answers(("f", " Ayşe "), ("l", "Yılmaz"), ("e", "Ayse@Example.com")), out _);

        Assert.NotNull(guest);
        Assert.Equal(("Ayşe", "Yılmaz", "ayse@example.com"), (guest.FirstName, guest.LastName, guest.Email));
    }

    [Fact]
    public void A_blank_name_or_an_address_without_at_yields_no_guest()
    {
        Assert.Null(ResponseGuest.From(IdentitySchema, Answers(("f", "Ayşe"), ("l", " "), ("e", "ayse@example.com")), out var blankName));
        Assert.Null(ResponseGuest.From(IdentitySchema, Answers(("f", "Ayşe"), ("l", "Yılmaz"), ("e", "ayse.example.com")), out var noAt));
        Assert.Equal((false, true), (blankName, noAt));
    }

    [Theory]
    [InlineData("ayse@example")]
    [InlineData("ayse@@example.com")]
    [InlineData("Ayşe <ayse@example.com>")]
    public void Identity_fields_take_only_an_address_the_recipient_list_would_mail(string email)
    {
        Assert.False(EmailAddress.IsValid(email));
        Assert.Null(ResponseGuest.From(IdentitySchema, Answers(("f", "Ayşe"), ("l", "Yılmaz"), ("e", email)), out var unmailable));
        Assert.True(unmailable);
    }

    [Theory]
    [InlineData("mailto:Ayse@Example.com")]
    [InlineData("<ayse@example.com>")]
    [InlineData("ayse@example.com,")]
    [InlineData("ayse@example.com; ")]
    [InlineData("ay\u200Bse@example.com\u200B")]
    public void Leftovers_of_a_pasted_address_are_dropped(string email)
    {
        var guest = ResponseGuest.From(IdentitySchema, Answers(("f", "Ayşe"), ("l", "Yılmaz"), ("e", email)), out _);

        Assert.Equal("ayse@example.com", guest?.Email);
    }

    [Fact]
    public void An_address_with_an_invisible_character_left_in_it_is_not_mailable() =>
        Assert.False(EmailAddress.IsValid("ayse@example.com\u200B"));

    [Theory]
    [InlineData("Ayse@Example.com", "ayse@example.com")]
    [InlineData("ayse@example", null)]
    [InlineData("ayşe @example.com", null)]
    public void The_marked_email_question_gives_an_address_only_guest(string answer, string? expected)
    {
        var guest = ResponseGuest.FromEmailQuestion([Field("e", "email")], Answers(("e", answer)));

        Assert.Equal(expected, guest?.Email);
        Assert.True(guest is null || !guest.HasName);
    }

    [Fact]
    public void An_overlong_address_is_refused()
    {
        var answer = new string('a', 250) + "@x.co";

        Assert.Null(ResponseGuest.FromEmailQuestion([Field("e", "email")], Answers(("e", answer))));
    }
}
