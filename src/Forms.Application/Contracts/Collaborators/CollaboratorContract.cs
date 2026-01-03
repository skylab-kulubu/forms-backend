using Forms.Domain.Enums;

namespace Forms.Application.Contracts.Collaborators;

public record FormCollaboratorContract(
    Guid UserId,
    CollaboratorRole Role
);
