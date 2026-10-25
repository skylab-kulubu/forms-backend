using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class WorkflowConcurrencyTests(FormsApiFactory api)
{
    [Fact]
    public async Task Sending_a_reviewed_step_several_times_at_once_keeps_one_answer_on_the_step()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var (apply, interview) = await TwoStepWorkflowAsync(owner);
        var applicant = api.Users.Add("Mehmet Kaya");
        var client = api.ClientFor(applicant.Id);
        (await client.SubmitAsync(apply.Id, ("name", "Mehmet"))).EnsureSuccessStatusCode();

        // Çift tıklama, iki sekme ya da mobil ağın yeniden denemesi: aynı adım aynı anda birkaç kez gelir.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.SubmitAsync(interview.Id, ("name", "Mehmet"))));

        Assert.All(attempts, attempt => Assert.Contains(attempt.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest, HttpStatusCode.Conflict }));
        Assert.Single(attempts, attempt => attempt.StatusCode == HttpStatusCode.Created);

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var answers = await db.Responses.Where(r => r.FormId == interview.Id && r.UserId == applicant.Id).Select(r => r.Id).ToListAsync();
        var onSteps = await db.WorkflowSteps.Where(s => s.ResponseId != null && answers.Contains(s.ResponseId.Value)).CountAsync();

        Assert.Single(answers);
        Assert.Equal(1, onSteps);
    }

    private static async Task<(FormContract Apply, FormContract Interview)> TwoStepWorkflowAsync(HttpClient owner)
    {
        var apply = await owner.CreateFormAsync(Api.NewForm("Başvuru", Api.Question("name")));
        var interview = await owner.CreateFormAsync(Api.NewForm("Mülakat", Api.Question("name")));

        await owner.PublishWorkflowAsync("Başvuru akışı", new WorkflowDefinitionRequest(
            [
                new WorkflowNodeRequest("apply", apply.Id, IsStart: true, RequiresManualReview: false),
                new WorkflowNodeRequest("interview", interview.Id, IsStart: false, RequiresManualReview: true)
            ],
            [
                new WorkflowTransitionRequest("apply", "interview", WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0),
                new WorkflowTransitionRequest("interview", null, WorkflowTransitionTrigger.ResponseApproved, null, Priority: 0),
                new WorkflowTransitionRequest("interview", null, WorkflowTransitionTrigger.ResponseDeclined, null, Priority: 0)
            ]));

        return (apply, interview);
    }
}
