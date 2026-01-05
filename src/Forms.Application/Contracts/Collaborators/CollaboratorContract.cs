using Forms.Application.Contracts.Auth;
using Forms.Domain.Enums;

namespace Forms.Application.Contracts.Collaborators;

public record FormCollaboratorContract(
    UserContract User,
    CollaboratorRole Role
);
