using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Workflows;

public sealed record WorkflowJourneyContract(
    string Title,
    int MaxSteps,
    IReadOnlyList<WorkflowJourneyStepContract> Route);

/// <param name="Revision">Bu adımda düzeltme istendiyse son isteğin notu; ilk gönderimde null.</param>
public sealed record WorkflowJourneyStepContract(
    int Stage,
    string? FormTitle,
    string Status,
    bool RequiresManualReview,
    bool Certain,
    DateTime? SubmittedAt = null,
    DateTime? ReviewedAt = null,
    string? ReviewNote = null,
    WorkflowRevisionContract? Revision = null);

/// <summary>Ekibin bir adımda istediği düzeltme; aday aynı formu yeniden gönderir.</summary>
/// <param name="Round">Adayın şimdi vereceği gönderimin numarası; ilk düzeltmede 2.</param>
/// <param name="QuestionIds">İnceleyenin işaretlediği sorular; işaret yoksa boş.</param>
/// <param name="Answers">Düzeltme istenen gönderimin cevapları; yalnız form gösterilirken dolar, form onlarla açılır.</param>
public sealed record WorkflowRevisionContract(
    int Round,
    string? Note,
    DateTime? ReturnedAt,
    IReadOnlyList<string> QuestionIds,
    IReadOnlyList<FormResponseSchemaItem>? Answers = null);

public sealed record WorkflowLastRunContract(
    WorkflowActionState State,
    string? ReviewNote,
    DateTime? ReviewedAt,
    WorkflowJourneyContract? Workflow);

public static class WorkflowJourneyStatus
{
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Declined = "declined";
    public const string InReview = "inReview";
    public const string Current = "current";
    public const string Upcoming = "upcoming";
    public const string TimedOut = "timedOut";
}
