using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Services.Attempts;

internal static class AttemptRouting
{
    /// <summary>Akış adayı sıradaki forma geçirdiyse o formun kimliği; mail "sıradaki adım" bağlantısını bununla kurar.</summary>
    public static Guid? NextFormOf(ServiceResult<WorkflowStepOutcome> routed) =>
        routed.Data is { State: WorkflowActionState.ShowForm, FormId: { } next } ? next : null;
}
