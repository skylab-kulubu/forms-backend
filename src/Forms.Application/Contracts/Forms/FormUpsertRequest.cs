using Forms.Domain.Enums;
using Forms.Domain.Models;
using Forms.Application.Contracts.Collaborators;

namespace Forms.Application.Contracts.Forms;

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