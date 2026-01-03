using Forms.Domain.Enums;

namespace Forms.Application.Contracts.Collaborators;

public record CollaboratorUpsertRequest(
    Guid UserId, 
    CollaboratorRole Role
);