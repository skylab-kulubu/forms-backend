using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ResponseDraftTests(FormsApiFactory api)
{
    // Postgres jsonb anahtarları kendi sırasına dizer ("question" önce gelir); istemci başka sırayla gönderir.
    private static FormSchemaItem NameQuestion(string label, string type = "short_text") =>
        new() { Id = "name", Type = type, Props = { ["required"] = true, ["question"] = label } };

    [Fact]
    public async Task Saving_the_form_without_breaking_a_question_keeps_everyones_half_answers()
    {
        var (owner, member, request, form) = await FormWithDraftAsync(NameQuestion("Adın?"));

        await owner.PutAsJsonAsync($"/api/admin/forms/{form.Id}", request);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/forms/responses/draft/{form.Id}")).StatusCode);

        await owner.PutAsJsonAsync($"/api/admin/forms/{form.Id}", request with { Schema = [NameQuestion("Adın nedir?")] });
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/forms/responses/draft/{form.Id}")).StatusCode);
    }

    [Fact]
    public async Task Changing_a_question_type_clears_the_half_answers()
    {
        var (owner, member, request, form) = await FormWithDraftAsync(NameQuestion("Adın?"));

        await owner.PutAsJsonAsync($"/api/admin/forms/{form.Id}", request with { Schema = [NameQuestion("Adın?", "long_text")] });

        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/forms/responses/draft/{form.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_timed_form_keeps_the_running_attempts_draft_even_when_a_question_type_changes()
    {
        var (owner, member, request, form) = await FormWithDraftAsync(NameQuestion("Adın?"), timeLimitMinutes: 30);

        await owner.PutAsJsonAsync($"/api/admin/forms/{form.Id}", request with { Schema = [NameQuestion("Adın?", "long_text")] });

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/forms/responses/draft/{form.Id}")).StatusCode);
    }

    private async Task<(HttpClient Owner, HttpClient Member, FormUpsertRequest Request, FormContract Form)> FormWithDraftAsync(
        FormSchemaItem question, int? timeLimitMinutes = null)
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var member = api.ClientFor(api.Users.Add("Mehmet Kaya").Id);
        var request = Api.NewForm("Kulüp başvurusu", question) with { TimeLimitMinutes = timeLimitMinutes };
        var form = await owner.CreateFormAsync(request);

        if (timeLimitMinutes is not null)
            (await member.PostAsync($"/api/forms/{form.Id}/attempt", null)).EnsureSuccessStatusCode();

        var saved = await member.PostAsJsonAsync("/api/forms/responses/draft", new ResponseDraftRequest(
            form.Id,
            [new FormResponseSchemaItem { Id = "name", Type = question.Type, Answer = "\"Mehmet\"" }],
            TimeSpent: 12));
        saved.EnsureSuccessStatusCode();

        return (owner, member, request, form);
    }
}
