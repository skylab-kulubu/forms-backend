using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Contracts;

/// <param name="Step">Legacy bağlı form akışının 1..5 aşaması; akış motorunda 0.</param>
/// <param name="State">Akış motoru işlemiyorsa null.</param>
/// <param name="Stage">Başvurunun kaçıncı adımında olduğu; grafikten değil adımdan gelir.</param>
public record FormDisplayPayload(
    FormDisplayContract? Form,
    int Step,
    string? ReviewNote = null,
    DateTime? ReviewedAt = null,
    Guid? InstanceId = null,
    WorkflowActionState? State = null,
    int Stage = 0,
    Guid? StartFormId = null
);
