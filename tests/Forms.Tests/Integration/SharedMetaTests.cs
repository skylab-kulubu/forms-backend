using System.Net.Http.Json;
using System.Text.Json;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class SharedMetaTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_shared_answers_public_card_names_the_sharer_without_their_address()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var responseId = (await (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId;
        var token = (await (await api.ClientFor(owner.Id).PostAsync($"/api/admin/forms/responses/{responseId}/share", null)).DataAsync<ShareTokenContract>()).Token;

        var sharedBy = await SharedByAsync($"/api/forms/responses/{responseId}/meta?token={token}");

        Assert.Equal("Ayşe Yılmaz", sharedBy.GetProperty("fullName").GetString());
        Assert.False(sharedBy.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task A_shared_templates_public_card_names_the_owner_without_their_address()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var client = api.ClientFor(owner.Id);
        var created = await client.PostAsJsonAsync("/api/admin/forms/component-groups", new ComponentGroupUpsertRequest(null, "Kimlik alanları", null, []));
        var groupId = (await created.DataAsync<ComponentGroupContract>()).Id;
        var token = (await (await client.PostAsync($"/api/admin/forms/component-groups/{groupId}/share", null)).DataAsync<ShareTokenContract>()).Token;

        var sharedBy = await SharedByAsync($"/api/forms/component-groups/{groupId}/meta?token={token}");

        Assert.Equal("Ayşe Yılmaz", sharedBy.GetProperty("fullName").GetString());
        Assert.False(sharedBy.TryGetProperty("email", out _));
    }

    private async Task<JsonElement> SharedByAsync(string url)
    {
        using var meta = JsonDocument.Parse(await api.CreateClient().GetStringAsync(url));

        return meta.RootElement.GetProperty("data").GetProperty("sharedBy").Clone();
    }
}
