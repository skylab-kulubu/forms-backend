using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Collaborators;

public record CollaboratorUpsertRequest(
    Guid UserId, 
    CollaboratorRole Role
);