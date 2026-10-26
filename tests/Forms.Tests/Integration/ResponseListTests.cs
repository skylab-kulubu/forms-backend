using System.Net.Http.Json;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;

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
