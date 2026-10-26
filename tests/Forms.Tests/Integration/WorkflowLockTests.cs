using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class WorkflowLockTests(FormsApiFactory api)
{
    [Fact]
    public async Task A_form_lists_only_the_locks_of_its_own_workflow_when_step_keys_repeat()
    {
        var owner = api.ClientFor(api.Users.Add("Ayşe Yılmaz").Id);
        var conditional = await PublishTwoStepAsync(owner, "Koşullu", new WorkflowConditionGroup
        {
            Rules = [new WorkflowConditionRule { QuestionId = "choice", Comparison = WorkflowConditionComparison.Equals, Value = "evet" }]
        });
        var plain = await PublishTwoStepAsync(owner, "Koşulsuz", condition: null);

        var page = await (await owner.GetAsync("/api/admin/forms/?pageSize=100")).DataAsync<PagedResult<FormSummaryContract>>();
        var locksOf = page.Items.ToDictionary(form => form.Id, form => form.Workflow?.LockedQuestions.Select(question => question.Id).ToList());

        // İki akış da adımlarına "apply" ve "interview" adını verdi; kilitler adım adına değil akışın sürümüne bağlı.
        Assert.Equal(["choice"], locksOf[conditional]);
        Assert.Empty(locksOf[plain]!);
    }

    private static async Task<Guid> PublishTwoStepAsync(HttpClient owner, string name, WorkflowConditionGroup? condition)
    {
        var apply = await owner.CreateFormAsync(Api.NewForm($"{name} başvuru", Api.Question("choice")));
        var interview = await owner.CreateFormAsync(Api.NewForm($"{name} mülakat", Api.Question("name")));

        List<WorkflowTransitionRequest> transitions =
        [
            new("apply", "interview", WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 1),
            new("interview", null, WorkflowTransitionTrigger.ResponseSubmitted, null, Priority: 0)
        ];
        if (condition is not null) transitions.Add(new("apply", null, WorkflowTransitionTrigger.ResponseSubmitted, condition, Priority: 0));

        await owner.PublishWorkflowAsync($"{name} akışı", new WorkflowDefinitionRequest(
            [
                new WorkflowNodeRequest("apply", apply.Id, IsStart: true, RequiresManualReview: false),
                new WorkflowNodeRequest("interview", interview.Id, IsStart: false, RequiresManualReview: false)
            ],
            transitions));

        return apply.Id;
    }
}
