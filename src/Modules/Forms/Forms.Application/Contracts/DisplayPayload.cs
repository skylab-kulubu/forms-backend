using Skylab.Forms.Application.Contracts.Forms;

namespace Skylab.Forms.Application.Contracts;

public record FormDisplayPayload(
    FormDisplayContract? Form,
    int Step,
    string? ReviewNote = null,
    DateTime? ReviewedAt = null
);