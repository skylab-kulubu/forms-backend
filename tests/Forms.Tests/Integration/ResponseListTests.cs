using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ResponseListTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_workflow_answer_is_listed_with_its_stage_and_round()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var apply = await owner.CreateFormAsync(Api.NewForm("Başvuru", Api.Question("name")));
        var interview = await owner.CreateFormAsync(Api.NewForm("Mülakat", Api.Question("name")));
        await owner.PublishWorkflowAsync("Başvuru akışı", new WorkflowDefinitionRequest(
            [
                new WorkflowNodeRequest("apply", apply.Id, IsStart: true, RequiresManualReview: true),
                new WorkflowNodeRequest("interview", interview.Id, IsStart: false, RequiresManualReview: false)
            ],
            [
                new WorkflowTransitionRequest("apply", "interview", WorkflowTransitionTrigger.ResponseApproved, null, Priority: 0),
                new WorkflowTransitionRequest("apply", null, WorkflowTransitionTrigger.ResponseDeclined, null, Priority: 0),
                new WorkflowTransitionRequest("interview", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)
            ]));
        var applicant = api.ClientFor(api.Users.Add("Mehmet Kaya").Id);

        var first = await SubmitAsync(applicant, apply.Id);
        await DecideAsync(owner, first, FormResponseStatus.Returned, "Adını düzelt");
        var second = await SubmitAsync(applicant, apply.Id);
        await DecideAsync(owner, second, FormResponseStatus.Approved);
        await SubmitAsync(applicant, interview.Id);

        Assert.Equal(new ResponseSummaryWorkflowContract(1, 2), (await RowsAsync(owner, apply.Id)).Single().Workflow);
        Assert.Equal(new ResponseSummaryWorkflowContract(1, 1), (await RowsAsync(owner, apply.Id, "&Status=6")).Single().Workflow);
        Assert.Equal(new ResponseSummaryWorkflowContract(2, 1), (await RowsAsync(owner, interview.Id)).Single().Workflow);
    }

    [Fact]
    public async Task A_started_timed_attempt_of_a_workflow_step_is_listed_with_its_stage()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var exam = await owner.CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        await owner.PublishWorkflowAsync("Sınav akışı", new WorkflowDefinitionRequest(
            [new WorkflowNodeRequest("exam", exam.Id, IsStart: true, RequiresManualReview: false)],
            [new WorkflowTransitionRequest("exam", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)]));
        (await api.ClientFor(api.Users.Add("Mehmet Kaya").Id).PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();

        var row = (await RowsAsync(owner, exam.Id)).Single();

        Assert.Equal(FormAttemptStatus.Started, row.Attempt?.Status);
        Assert.Equal(new ResponseSummaryWorkflowContract(1, 1), row.Workflow);
    }

    [Fact]
    public async Task An_answer_outside_a_workflow_is_listed_without_one()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        await SubmitAsync(api.ClientFor(api.Users.Add("Mehmet Kaya").Id), form.Id);

        Assert.Null((await RowsAsync(owner, form.Id)).Single().Workflow);
    }

    [Fact]
    public async Task A_response_is_found_on_the_page_the_list_shows_it_on()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var oldest = await SubmitAsync(api.ClientFor(api.Users.Add("Mehmet Kaya").Id), form.Id);
        var middle = await SubmitAsync(api.ClientFor(api.Users.Add("Zeynep Ak").Id), form.Id);
        var newest = await SubmitAsync(api.ClientFor(api.Users.Add("Can Demir").Id), form.Id);
        await SubmittedAtAsync(oldest, DateTime.UtcNow.AddHours(-3));
        await SubmittedAtAsync(middle, DateTime.UtcNow.AddHours(-2));
        await SubmittedAtAsync(newest, DateTime.UtcNow.AddHours(-1));

        var page = await PageOfAsync(owner, form.Id, oldest, "PageSize=2");
        var listed = await (await owner.GetAsync($"/api/admin/forms/{form.Id}/responses?PageSize=2&Page={page}")).DataAsync<FormResponsesListResult>();

        Assert.Equal(2, page);
        Assert.Contains(oldest, listed.PaginationData.Items.Select(row => row.Id));
        Assert.Equal(1, await PageOfAsync(owner, form.Id, oldest, "PageSize=2&SortingDirection=ascending"));
        Assert.Equal(1, await PageOfAsync(owner, form.Id, newest, "PageSize=2"));
    }

    [Fact]
    public async Task A_response_the_list_leaves_out_has_no_page()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var form = await owner.CreateFormAsync(Api.NewForm("Kulüp başvurusu", Api.Question("name")));
        var other = await owner.CreateFormAsync(Api.NewForm("Başka form", Api.Question("name")));
        var responseId = await SubmitAsync(api.ClientFor(api.Users.Add("Mehmet Kaya").Id), form.Id);

        var approvedOnly = await owner.GetAsync($"/api/admin/forms/{form.Id}/responses/{responseId}/page?Status={(int)FormResponseStatus.Approved}");
        var otherForm = await owner.GetAsync($"/api/admin/forms/{other.Id}/responses/{responseId}/page");

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (approvedOnly.StatusCode, otherForm.StatusCode));
    }

    private static async Task<int> PageOfAsync(HttpClient owner, Guid formId, Guid responseId, string query) =>
        (await (await owner.GetAsync($"/api/admin/forms/{formId}/responses/{responseId}/page?{query}")).DataAsync<ResponseListPageContract>()).Page;

    private async Task SubmittedAtAsync(Guid responseId, DateTime submittedAt)
    {
        using var scope = api.Services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Responses
            .Where(response => response.Id == responseId)
            .ExecuteUpdateAsync(set => set.SetProperty(response => response.SubmittedAt, submittedAt));
    }

    private static async Task<List<ResponseSummaryContract>> RowsAsync(HttpClient owner, Guid formId, string filter = "") =>
        (await (await owner.GetAsync($"/api/admin/forms/{formId}/responses?Page=1{filter}")).DataAsync<FormResponsesListResult>()).PaginationData.Items;

    private static async Task<Guid> SubmitAsync(HttpClient applicant, Guid formId)
    {
        var submitted = await applicant.SubmitAsync(formId, ("name", "Aday"));
        submitted.EnsureSuccessStatusCode();

        return (await submitted.DataAsync<ResponseSubmitResult>()).ResponseId!.Value;
    }

    private static async Task DecideAsync(HttpClient owner, Guid responseId, FormResponseStatus status, string? note = null) =>
        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = status, note })).EnsureSuccessStatusCode();
}
