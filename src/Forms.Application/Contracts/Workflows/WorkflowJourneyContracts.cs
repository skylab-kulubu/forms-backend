namespace Skylab.Forms.Application.Contracts.Workflows;

public sealed record WorkflowJourneyContract(
    string Title,
    int MaxSteps,
    IReadOnlyList<WorkflowJourneyStepContract> Route);

public sealed record WorkflowJourneyStepContract(
    int Stage,
    string? FormTitle,
    string Status,
    bool RequiresManualReview,
    bool Certain,
    DateTime? SubmittedAt = null,
    DateTime? ReviewedAt = null,
    string? ReviewNote = null);

public static class WorkflowJourneyStatus
{
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Declined = "declined";
    public const string InReview = "inReview";
    public const string Current = "current";
    public const string Upcoming = "upcoming";
}
