using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Services.Attempts;

internal static class AttemptRouting
{
    public static Guid? NextFormOf(ServiceResult<WorkflowStepOutcome> routed) =>
        routed.Data is { State: WorkflowActionState.ShowForm, FormId: { } next } ? next : null;
}
