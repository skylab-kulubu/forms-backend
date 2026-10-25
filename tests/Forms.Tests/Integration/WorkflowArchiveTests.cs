using System.Net;
using System.Net.Http.Json;
using Forms.Infrastructure.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class WorkflowArchiveTests(FormsApiFactory api)
{
    [Fact]
    public async Task Archiving_stops_whoever_must_fill_a_form_and_keeps_waiting_answers_reviewable()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var (workflowId, apply, interview) = await ReviewThenInterviewAsync(owner);

        var waiting = api.Users.Add("Mehmet Kaya");
        var waitingResponse = await SubmitAsync(waiting.Id, apply.Id);

        var moving = api.Users.Add("Zeynep Ak");
        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{await SubmitAsync(moving.Id, apply.Id)}/status", new { newStatus = FormResponseStatus.Approved })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/admin/workflows/{workflowId}")).StatusCode);

        // Formu dolduracak kişinin başvurusu durur; formlar kapanır, eski 2. adım linki de bir şey açmaz.
        Assert.Equal(WorkflowInstanceStatus.Terminated, await RunStatusAsync(workflowId, moving.Id));
        Assert.Equal(FormStatus.Closed, (await (await owner.GetAsync($"/api/admin/forms/{interview.Id}")).DataAsync<FormContract>()).Status);
        Assert.Equal(FormStatus.Closed, (await (await owner.GetAsync($"/api/admin/forms/{apply.Id}")).DataAsync<FormContract>()).Status);

        // İnceleme bekleyen cevap incelenir; başvuru sonraki adıma geçmeden orada durur.
        Assert.Equal(WorkflowInstanceStatus.Active, await RunStatusAsync(workflowId, waiting.Id));
        var reviewed = await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{waitingResponse}/status", new { newStatus = FormResponseStatus.Approved });

        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal(WorkflowInstanceStatus.Terminated, await RunStatusAsync(workflowId, waiting.Id));
        Assert.Equal(1, await StepCountAsync(workflowId, waiting.Id));
    }

    [Fact]
    public async Task An_archived_workflow_cannot_ask_for_changes_on_a_waiting_answer()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var (workflowId, apply, _) = await ReviewThenInterviewAsync(owner);
        var waitingResponse = await SubmitAsync(api.Users.Add("Mehmet Kaya").Id, apply.Id);
        await owner.DeleteAsync($"/api/admin/workflows/{workflowId}");

        var returned = await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{waitingResponse}/status", new { newStatus = FormResponseStatus.Returned, note = "Adını düzelt" });

        Assert.Equal(HttpStatusCode.BadRequest, returned.StatusCode);
    }

    [Fact]
    public async Task The_migration_stops_the_runs_an_earlier_archive_left_waiting_for_their_applicant()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var (workflowId, apply, _) = await ReviewThenInterviewAsync(owner);
        var waiting = api.Users.Add("Mehmet Kaya");
        await SubmitAsync(waiting.Id, apply.Id);
        var moving = api.Users.Add("Zeynep Ak");
        (await owner.PatchAsJsonAsync($"/api/admin/forms/responses/{await SubmitAsync(moving.Id, apply.Id)}/status", new { newStatus = FormResponseStatus.Approved })).EnsureSuccessStatusCode();

        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

            // Eski arşivleme yalnız bayrakları değiştirirdi.
            await db.Workflows.Where(w => w.Id == workflowId).ExecuteUpdateAsync(s => s
                .SetProperty(w => w.Status, WorkflowStatus.Archived)
                .SetProperty(w => w.Intake, WorkflowIntake.Closed));

            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(new StopRunsOfArchivedWorkflows().UpOperations, db.Model);
            foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command.CommandText);
        }

        Assert.Equal(WorkflowInstanceStatus.Terminated, await RunStatusAsync(workflowId, moving.Id));
        Assert.Equal(WorkflowInstanceStatus.Active, await RunStatusAsync(workflowId, waiting.Id));
    }

    private async Task<Guid> SubmitAsync(Guid userId, Guid formId)
    {
        var submitted = await api.ClientFor(userId).SubmitAsync(formId, ("name", "Aday"));
        submitted.EnsureSuccessStatusCode();

        return (await submitted.DataAsync<ResponseSubmitResult>()).ResponseId!.Value;
    }

    private async Task<WorkflowInstanceStatus> RunStatusAsync(Guid workflowId, Guid userId)
    {
        using var scope = api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<FormsDbContext>().WorkflowInstances
            .Where(run => run.WorkflowId == workflowId && run.UserId == userId)
            .Select(run => run.Status)
            .SingleAsync();
    }

    private async Task<int> StepCountAsync(Guid workflowId, Guid userId)
    {
        using var scope = api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<FormsDbContext>().WorkflowSteps
            .CountAsync(step => step.WorkflowInstance.WorkflowId == workflowId && step.WorkflowInstance.UserId == userId);
    }

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
