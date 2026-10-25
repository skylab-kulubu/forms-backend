using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class FormLifecycleTests(FormsApiFactory api)
{
    private sealed record ResponseDetail(Guid Id, List<FormResponseSchemaItem> Schema);

    [Fact]
    public async Task An_owner_reads_the_answer_a_member_sends()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var member = api.Users.Add("Mehmet Kaya");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));

        var submitted = await api.ClientFor(member.Id).SubmitAsync(form.Id, ("name", "Mehmet"));

        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);

        var list = await (await api.ClientFor(owner.Id).GetAsync($"/api/admin/forms/{form.Id}/responses")).DataAsync<FormResponsesListResult>();
        var summary = Assert.Single(list.PaginationData.Items);
        var detail = await (await api.ClientFor(owner.Id).GetAsync($"/api/admin/forms/responses/{summary.Id}")).DataAsync<ResponseDetail>();

        Assert.Equal("Mehmet Kaya", summary.User?.FullName);
        Assert.Equal("Mehmet", Assert.Single(detail.Schema, item => item.Id == "name").Answer);
    }

    [Fact]
    public async Task A_stranger_cannot_list_the_answers()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var stranger = api.Users.Add("Can Demir");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));

        var list = await api.ClientFor(stranger.Id).GetAsync($"/api/admin/forms/{form.Id}/responses");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    public static TheoryData<string> UnverifiableTokens => new()
    {
        "not-a-token",
        TestTokens.Create(Guid.NewGuid(), new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-key-the-forms-api-has-never-seen-0123")), DateTime.UtcNow.AddHours(1)),
        TestTokens.Create(Guid.NewGuid(), TestTokens.SigningKey, DateTime.UtcNow.AddMinutes(-10))
    };

    [Theory]
    [MemberData(nameof(UnverifiableTokens))]
    public async Task A_token_the_api_cannot_verify_is_refused_instead_of_treated_as_anonymous(string token)
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var submitted = await client.PostAsJsonAsync("/api/forms/responses", new { formId = Guid.NewGuid(), responses = Array.Empty<object>(), timeSpent = 0 });

        Assert.Equal(HttpStatusCode.Unauthorized, submitted.StatusCode);
    }
}
