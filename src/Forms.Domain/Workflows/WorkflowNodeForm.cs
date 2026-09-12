namespace Skylab.Forms.Domain.Workflows;

/// <summary>
/// Graf doğrulamasının bir node formu hakkında bilmesi gereken her şey. Domain'in
/// depolamaya bağımlı olmaması için form bu izdüşümle aktarılır.
/// </summary>
public sealed record WorkflowNodeForm(
    bool Exists,
    bool IsOpen,
    bool RequiresManualReview,
    bool AllowAnonymousResponses,
    bool WorkflowOwnerIsFormOwner,
    IReadOnlyCollection<string> QuestionIds)
{
    public static readonly WorkflowNodeForm Missing =
        new(false, false, false, false, false, Array.Empty<string>());
}
