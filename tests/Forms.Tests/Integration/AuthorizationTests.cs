using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthorizationTests(FormsApiFactory api)
{
    [Fact]
    public async Task Only_a_platform_admin_lists_every_form()
    {
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(Guid.NewGuid(), "skyforms:*").GetAsync("/api/admin/forms/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(Guid.NewGuid(), "skyforms:account:erase").GetAsync("/api/admin/forms/all")).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/forms/")]
    [InlineData("/api/admin/workflows/")]
    [InlineData("/api/forms/responses/draft/3f2b9c56-8f52-4d6e-9a43-1f4c55f0f1a2")]
    public async Task A_request_without_a_token_gets_401_in_the_service_envelope(string url)
    {
        var response = await api.CreateClient().GetAsync(url);
        var body = await response.Content.ReadFromJsonAsync<ServiceResult<object>>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(ServiceStatus.Unauthorized, body?.Status);
    }

    [Fact]
    public async Task A_response_viewer_decides_on_answers_but_cannot_edit_the_form()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var viewer = api.Users.Add("Can Demir");
        var request = Api.NewForm("Kulüp başvurusu", Api.Question("name")) with
        {
            RequiresManualReview = true,
            Collaborators = [new CollaboratorUpsertRequest(viewer.Id, CollaboratorRole.Viewer)]
        };
        var form = await api.ClientFor(owner.Id).CreateFormAsync(request);
        var responseId = (await (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId;

        var reviewed = await api.ClientFor(viewer.Id).PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = FormResponseStatus.Approved });
        var edited = await api.ClientFor(viewer.Id).PutAsJsonAsync($"/api/admin/forms/{form.Id}", request with { Title = "Değişti" });

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden), (reviewed.StatusCode, edited.StatusCode));
    }

    [Fact]
    public async Task A_platform_admin_reads_answers_but_does_not_decide_on_them()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")) with { RequiresManualReview = true });
        var responseId = (await (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).SubmitAsync(form.Id, ("name", "Mehmet"))).DataAsync<ResponseSubmitResult>()).ResponseId;
        var admin = api.ClientFor(Guid.NewGuid(), "skyforms:*");

        var read = await admin.GetAsync($"/api/admin/forms/responses/{responseId}");
        var reviewed = await admin.PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = FormResponseStatus.Approved });

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.Forbidden), (read.StatusCode, reviewed.StatusCode));
    }

    [Fact]
    public async Task A_platform_admin_reads_a_workflow_it_may_not_edit()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var workflowId = await owner.CreateWorkflowAsync("Başvuru akışı");
        var admin = api.ClientFor(Guid.NewGuid(), "skyforms:*");

        var asOwner = await (await owner.GetAsync($"/api/admin/workflows/{workflowId}")).DataAsync<WorkflowContract>();
        var asAdmin = await (await admin.GetAsync($"/api/admin/workflows/{workflowId}")).DataAsync<WorkflowContract>();
        var renamed = await admin.PutAsJsonAsync($"/api/admin/workflows/{workflowId}", new WorkflowUpsertRequest("Değişti", null, false));

        Assert.Equal((true, false), (asOwner.CanEdit, asAdmin.CanEdit));
        Assert.Equal(HttpStatusCode.Forbidden, renamed.StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_deletes_a_form()
    {
        var owner = api.Users.Add("Ayşe Yılmaz");
        var editor = api.Users.Add("Can Demir");
        var form = await api.ClientFor(owner.Id).CreateFormAsync(Api.NewForm("Kulüp başvurusu") with
        {
            Collaborators = [new CollaboratorUpsertRequest(editor.Id, CollaboratorRole.Editor)]
        });

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(editor.Id).DeleteAsync($"/api/admin/forms/{form.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.ClientFor(owner.Id).DeleteAsync($"/api/admin/forms/{form.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_token_whose_subject_is_not_a_guid_counts_as_not_signed_in()
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.CreateWithSubject("service-account-without-guid"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/forms/")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_member_gets_403_in_the_service_envelope_from_an_admin_list()
    {
        var response = await api.ClientFor(Guid.NewGuid()).GetAsync("/api/admin/workflows/orphaned");
        var body = await response.Content.ReadFromJsonAsync<ServiceResult<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ServiceStatus.NotAuthorized, body?.Status);
    }
}
