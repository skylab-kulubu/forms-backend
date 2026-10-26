using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class WorkflowAdmissionTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_running_application_turns_a_second_one_through_a_new_start_form_away_as_running()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var apply = await owner.CreateFormAsync(Api.NewForm("Başvuru", Api.Question("name")));
        var interview = await owner.CreateFormAsync(Api.NewForm("Mülakat", Api.Question("name")));
        // Tekrar başvuruya açık akış: tek başvuru kuralı burada ikinci başvuruyu durdurmaz.
        var workflowId = await owner.PublishWorkflowAsync("Başvuru akışı", TwoSteps(apply.Id, interview.Id), allowMultipleRuns: true);

        var applicant = api.Users.Add("Mehmet Kaya");
        var client = api.ClientFor(applicant.Id);
        (await client.SubmitAsync(apply.Id, ("name", "Mehmet"))).EnsureSuccessStatusCode();

        // Akış başka bir başlangıç formuyla yeniden yayımlanır; adayın başvurusu eski sürümde sürüyor.
        var newApply = await owner.CreateFormAsync(Api.NewForm("Yeni başvuru", Api.Question("name")));
        var newInterview = await owner.CreateFormAsync(Api.NewForm("Yeni mülakat", Api.Question("name")));
        (await owner.PutAsJsonAsync($"/api/admin/workflows/{workflowId}/definition", TwoSteps(newApply.Id, newInterview.Id))).EnsureSuccessStatusCode();
        (await owner.PostAsync($"/api/admin/workflows/{workflowId}/publish", null)).EnsureSuccessStatusCode();

        var second = await client.SubmitAsync(newApply.Id, ("name", "Mehmet"));

        using var scope = api.Services.CreateScope();
        var running = await scope.ServiceProvider.GetRequiredService<FormsDbContext>().WorkflowInstances
            .CountAsync(run => run.WorkflowId == workflowId && run.UserId == applicant.Id && run.Status == WorkflowInstanceStatus.Active);

        // Veritabanının tekil indeksi ikinci başvuruyu zaten durdururdu, ama "kayıt aynı anda değişti" diyen bir 409 ile.
        var body = await second.Content.ReadFromJsonAsync<ServiceResult<ResponseSubmitResult>>();
        Assert.Equal((ServiceStatus.RequiresParentApproval, WorkflowActionState.RequiresPreviousStep), (body?.Status, body?.Data?.State));
        Assert.Equal(1, running);
    }

    private static WorkflowDefinitionRequest TwoSteps(Guid first, Guid second) => new(
        [
            new WorkflowNodeRequest("apply", first, IsStart: true, RequiresManualReview: false),
            new WorkflowNodeRequest("interview", second, IsStart: false, RequiresManualReview: false)
        ],
        [
            new WorkflowTransitionRequest("apply", "interview", WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0),
            new WorkflowTransitionRequest("interview", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)
        ]);
}
