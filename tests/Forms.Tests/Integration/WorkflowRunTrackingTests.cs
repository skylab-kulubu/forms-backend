using System.Net.Http.Json;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class WorkflowRunTrackingTests(FormsApiFactory api)
{
    [Fact]
    public async Task Runs_are_listed_by_the_state_they_are_in()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var (workflowId, apply, interview) = await ReviewThenInterviewAsync(owner);

        var waiting = await ApplyAsync(apply.Id, "Mehmet Kaya");
        var moving = await ApplyAsync(apply.Id, "Zeynep Ak");
        await DecideAsync(owner, moving.ResponseId, FormResponseStatus.Approved);
        var revising = await ApplyAsync(apply.Id, "Can Demir");
        await DecideAsync(owner, revising.ResponseId, FormResponseStatus.Returned, "Adını düzelt");
        var declined = await ApplyAsync(apply.Id, "Elif Şahin");
        await DecideAsync(owner, declined.ResponseId, FormResponseStatus.Declined);
        var done = await ApplyAsync(apply.Id, "Deniz Arslan");
        await DecideAsync(owner, done.ResponseId, FormResponseStatus.Approved);
        (await api.ClientFor(done.UserId).SubmitAsync(interview.Id, ("name", "Deniz"))).EnsureSuccessStatusCode();

        Assert.Equal([waiting.UserId], await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Active, WorkflowRunState.AwaitingReview));
        Assert.Equal([moving.UserId], await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Active, WorkflowRunState.AwaitingApplicant));
        Assert.Equal([revising.UserId], await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Active, WorkflowRunState.RevisionRequested));
        Assert.Equal([declined.UserId], await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Finished, WorkflowRunState.Declined));
        Assert.Equal([done.UserId], await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Finished, WorkflowRunState.Completed));
        Assert.Empty(await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Finished, WorkflowRunState.AwaitingReview));
    }

    [Fact]
    public async Task A_started_timed_step_is_listed_as_its_timer_running()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var exam = await owner.CreateFormAsync(Api.NewForm("Sınav", Api.Question("name")) with { TimeLimitMinutes = 30 });
        var workflowId = await owner.PublishWorkflowAsync("Sınav akışı", new WorkflowDefinitionRequest(
            [new WorkflowNodeRequest("exam", exam.Id, IsStart: true, RequiresManualReview: false)],
            [new WorkflowTransitionRequest("exam", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)]));
        var candidate = api.Users.Add("Mehmet Kaya");
        (await api.ClientFor(candidate.Id).PostAsync($"/api/forms/{exam.Id}/attempt", null)).EnsureSuccessStatusCode();

        Assert.Equal([candidate.Id], await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Active, WorkflowRunState.TimerRunning));
        Assert.Empty(await ApplicantsAsync(owner, workflowId, WorkflowRunFilter.Active, WorkflowRunState.AwaitingApplicant));
    }

    private static async Task<List<Guid>> ApplicantsAsync(HttpClient owner, Guid workflowId, WorkflowRunFilter filter, WorkflowRunState state)
    {
        var page = await (await owner.GetAsync($"/api/admin/workflows/{workflowId}/runs?Filter={(int)filter}&State={(int)state}"))
            .DataAsync<PagedResult<WorkflowRunContract>>();

        Assert.All(page.Items, run => Assert.Equal(state, run.State));

        return [.. page.Items.Select(run => run.Applicant.Id)];
    }

    private async Task<(Guid UserId, Guid ResponseId)> ApplyAsync(Guid formId, string name)
    {
        var applicant = api.Users.Add(name);
        var submitted = await api.ClientFor(applicant.Id).SubmitAsync(formId, ("name", name));
        submitted.EnsureSuccessStatusCode();

        return (applicant.Id, (await submitted.DataAsync<ResponseSubmitResult>()).ResponseId!.Value);
    }

    private static async Task DecideAsync(HttpClient owner, Guid responseId, FormResponseStatus status, string? note = null) =>
        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{responseId}/status", new { newStatus = status, note })).EnsureSuccessStatusCode();

    private static async Task<(Guid WorkflowId, FormContract Apply, FormContract Interview)> ReviewThenInterviewAsync(HttpClient owner)
    {
        var apply = await owner.CreateFormAsync(Api.NewForm("Başvuru", Api.Question("name")));
        var interview = await owner.CreateFormAsync(Api.NewForm("Mülakat", Api.Question("name")));

        var workflowId = await owner.PublishWorkflowAsync("Başvuru akışı", new WorkflowDefinitionRequest(
            [
                new WorkflowNodeRequest("apply", apply.Id, IsStart: true, RequiresManualReview: true),
                new WorkflowNodeRequest("interview", interview.Id, IsStart: false, RequiresManualReview: false)
            ],
            [
                new WorkflowTransitionRequest("apply", "interview", WorkflowTransitionTrigger.ResponseApproved, null, Priority: 0),
                new WorkflowTransitionRequest("apply", null, WorkflowTransitionTrigger.ResponseDeclined, null, Priority: 0),
                new WorkflowTransitionRequest("interview", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)
            ]));

        return (workflowId, apply, interview);
    }
}
