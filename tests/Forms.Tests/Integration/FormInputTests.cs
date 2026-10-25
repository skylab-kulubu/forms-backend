using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class FormInputTests(FormsApiFactory api)
{
    [Theory]
    [InlineData(FormStatus.Deleted)]
    [InlineData((FormStatus)7)]
    public async Task An_editor_cannot_move_a_form_into_a_status_outside_open_and_closed(FormStatus status)
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var editor = api.Users.Add("Can Demir");
        var request = Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { Collaborators = [new CollaboratorUpsertRequest(editor.Id, CollaboratorRole.Editor)] };
        var form = await api.ClientFor(owner.Id).CreateFormAsync(request);

        var updated = await api.ClientFor(editor.Id).PutAsJsonAsync($"/api/admin/forms/{form.Id}", request with { Status = status });

        Assert.Equal(HttpStatusCode.BadRequest, updated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(owner.Id).GetAsync($"/api/admin/forms/{form.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_form_cannot_be_created_deleted()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");

        var created = await api.ClientFor(owner.Id).PostAsJsonAsync("/api/admin/forms/", Api.NewForm("Kulüp başvurusu") with { Status = FormStatus.Deleted });

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
    }

    [Fact]
    public async Task A_reviewer_cannot_set_a_status_the_code_does_not_know()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var member = api.Users.Add("Mehmet Kaya");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var responseId = (await (await api.ClientFor(member.Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId;

        var reviewed = await api.ClientFor(owner.Id).PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = 42, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, reviewed.StatusCode);
    }
}
