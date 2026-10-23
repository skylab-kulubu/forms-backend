using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Contracts.Workflows;

public enum WorkflowRunState
{
    AwaitingApplicant = 0,
    TimerRunning = 1,
    RevisionRequested = 2,
    AwaitingReview = 3,
    Completed = 4,
    Declined = 5,
    TimedOut = 6,
    Voided = 7,
    Stopped = 8
}

public record WorkflowRunOverviewContract(
    int ActiveRunCount,
    int AwaitingReviewCount,
    List<WorkflowRunStepContract> Steps,
    WorkflowRunFinishedContract Finished
);

public record WorkflowRunStepContract(
    string NodeKey,
    Guid FormId,
    string FormTitle,
    bool IsStart,
    bool RequiresManualReview,
    int Depth,
    bool IsRemoved,
    int Count,
    int AwaitingReview,
    int AwaitingApplicant,
    int TimerRunning,
    int RevisionRequested
);

public record WorkflowRunFinishedContract(
    int Completed,
    int Declined,
    int TimedOut,
    int Voided,
    int Stopped
);

public record WorkflowRunContract(
    Guid Id,
    UserContract Applicant,
    WorkflowRunState State,
    string NodeKey,
    Guid FormId,
    string FormTitle,
    int Sequence,
    int Round,
    DateTime StartedAt,
    DateTime StepStartedAt,
    DateTime? DeadlineAt,
    DateTime? CompletedAt,
    WorkflowRunResponseContract? LatestResponse
);

public record WorkflowRunResponseContract(Guid Id, Guid FormId);
