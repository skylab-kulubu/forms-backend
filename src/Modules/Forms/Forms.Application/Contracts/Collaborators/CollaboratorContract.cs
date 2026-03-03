using Skylab.Forms.Application.Contracts.Auth;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Collaborators;

public record FormCollaboratorContract(
    UserContract User,
    CollaboratorRole Role
);
