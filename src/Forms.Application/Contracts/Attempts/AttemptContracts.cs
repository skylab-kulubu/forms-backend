using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Attempts;

public static class FormAttemptState
{
    public const string NotStarted = "notStarted";
    public const string StartClosed = "startClosed";
    public const string Running = "running";
    public const string Provisional = "provisional";
    public const string NoSubmission = "noSubmission";
    public const string Submitted = "submitted";
}

public static class FormClosedReason
{
    public const string Closed = "closed";
    public const string StartClosed = "startClosed";
    public const string NotStarted = "notStarted";
    public const string TimeUp = "timeUp";
}

public record FormAttemptExtensionContract(int Minutes, DateTime At);

public record FormAttemptDisplayContract(
    string State,
    int TimeLimitMinutes,
    DateTime? StartClosesAt,
    DateTime? StartedAt,
    DateTime? DeadlineAt,
    int ExtendedMinutes,
    FormAttemptExtensionContract? LastExtension,
    bool ClosedByTeam,
    bool HadDraft,
    IReadOnlyList<string>? Deliverables = null,
    Guid? NextFormId = null
);

public record FormAttemptStartResult(FormAttemptDisplayContract Attempt, DateTime ServerNow, Guid? InstanceId);

public record FormAttemptEventContract(
    Guid Id,
    FormAttemptEventType Type,
    UserContract? Actor,
    int? Minutes,
    string? Note,
    DateTime? DeadlineAt,
    DateTime CreatedAt
);

public record FormAttemptRouteContract(bool EndsFlow, Guid? FormId, string? FormTitle);

public record FormAttemptDetailContract(
    Guid Id,
    FormAttemptStatus Status,
    Guid? ResponseId,
    DateTime OpenedAt,
    DateTime? StartedAt,
    DateTime? DeadlineAt,
    DateTime? ExpiredAt,
    DateTime? SubmittedAt,
    int TimeLimitMinutes,
    int ExtendedMinutes,
    bool ClosedByTeam,
    bool CanExtend,
    bool CanDecide,
    bool CanRemind,
    DateTime? ReminderSentAt,
    bool AcceptRequiresReview,
    FormAttemptRouteContract? OnAccept,
    FormAttemptRouteContract? OnClose,
    List<FormAttemptEventContract> Events
);

public record FormAttemptViewContract(
    Guid Id,
    Guid FormId,
    UserContract? User,
    FormAttemptDetailContract Attempt,
    FormTask? Task,
    ResponseWorkflowContract? Workflow
);

public record AttemptExtendRequest(int Minutes, string? Note);

public record AttemptDecisionRequest(string? Note);

public record FormAttemptActorStatContract(UserContract Actor, int Count, int Minutes);

public record FormAttemptStalledQuestionContract(string QuestionId, string Question, int Number, int Count, bool IsRequired);

public record FormAttemptAnalyticsContract(
    int TimeLimitMinutes,
    int Opened,
    int Started,
    int Submitted,
    int Provisional,
    int Running,
    int NoSubmission,
    List<int> DurationMinutes,
    int ExtendedPeople,
    int ExtendedMinutes,
    List<FormAttemptActorStatContract> ExtendedBy,
    FormAttemptStalledQuestionContract? StalledQuestion,
    int ExpiredWithDraft
);
