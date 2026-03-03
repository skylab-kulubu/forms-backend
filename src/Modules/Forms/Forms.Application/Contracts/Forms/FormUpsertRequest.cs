using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Collaborators;

namespace Skylab.Forms.Application.Contracts.Forms;

public record FormUpsertRequest(
    Guid? Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    bool AllowAnonymousResponses,
    bool AllowMultipleResponses,
    bool RequiresManualReview,
    FormStatus Status,
    Guid? LinkedFormId,
    List<CollaboratorUpsertRequest>? Collaborators
);