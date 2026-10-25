using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Tests.Application;

public class WorkflowRunVoidBlockTests
{
    private static WorkflowRunFacts Run(
        WorkflowInstanceStatus status = WorkflowInstanceStatus.Active,
        WorkflowStatus workflow = WorkflowStatus.Published,
        bool timedStep = false,
        bool multipleRuns = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Başvuru", workflow, multipleRuns, WorkflowIntake.Open,
            null, status, WorkflowInstanceOutcome.None, null, null, null, null, Guid.NewGuid(), timedStep);

    [Fact]
    public void A_running_application_of_a_live_workflow_can_be_voided()
    {
        Assert.Null(WorkflowRunVoidBlock.Of(Run()));
        Assert.Null(WorkflowRunVoidBlock.Of(Run(multipleRuns: true)));
    }

    [Fact]
    public void Blocks_are_checked_in_order()
    {
        Assert.Equal(WorkflowRunVoidBlock.Voided, WorkflowRunVoidBlock.Of(Run(WorkflowInstanceStatus.Voided, WorkflowStatus.Archived)));
        Assert.Equal(WorkflowRunVoidBlock.WorkflowArchived, WorkflowRunVoidBlock.Of(Run(workflow: WorkflowStatus.Archived, timedStep: true)));
        Assert.Equal(WorkflowRunVoidBlock.TimedStep, WorkflowRunVoidBlock.Of(Run(timedStep: true)));
        Assert.Equal(WorkflowRunVoidBlock.Repeatable, WorkflowRunVoidBlock.Of(Run(WorkflowInstanceStatus.Completed, multipleRuns: true)));
    }
}
