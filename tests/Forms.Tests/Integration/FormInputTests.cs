using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
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
    public async Task Names_longer_than_their_column_are_refused_with_400()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var name = new string('a', 101);

        var form = await owner.PostAsJsonAsync("/api/admin/forms/", Api.NewForm(name));
        var workflow = await owner.PostAsJsonAsync("/api/admin/workflows/", new WorkflowUpsertRequest(name, null, AllowMultipleRuns: false));
        var template = await owner.PostAsJsonAsync("/api/admin/forms/component-groups", new ComponentGroupUpsertRequest(null, name, null, []));

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (form.StatusCode, workflow.StatusCode, template.StatusCode));
    }

    [Fact]
    public async Task A_draft_workflow_definition_is_checked_before_the_database_refuses_it()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Başvuru", Api.Question("name")));
        var other = await owner.CreateFormAsync(Api.NewForm("Mülakat", Api.Question("name")));
        var workflowId = await owner.CreateWorkflowAsync("Başvuru akışı");

        var longKey = await owner.PutAsJsonAsync($"/api/admin/workflows/{workflowId}/definition", new WorkflowDefinitionRequest(
            [new WorkflowNodeRequest(new string('k', 65), form.Id, IsStart: true)],
            []));

        var samePriority = await owner.PutAsJsonAsync($"/api/admin/workflows/{workflowId}/definition", new WorkflowDefinitionRequest(
            [new WorkflowNodeRequest("apply", form.Id, IsStart: true), new WorkflowNodeRequest("interview", other.Id, IsStart: false)],
            [
                new WorkflowTransitionRequest("apply", "interview", WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0),
                new WorkflowTransitionRequest("apply", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)
            ]));

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (longKey.StatusCode, samePriority.StatusCode));
    }

    [Theory]
    [InlineData("/api/admin/forms/?Page=0&PageSize=10")]
    [InlineData("/api/admin/forms/component-groups?Page=-3")]
    [InlineData("/api/admin/workflows/?Page=0")]
    public async Task A_page_below_one_reads_the_first_page(string url)
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url)).StatusCode);
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
