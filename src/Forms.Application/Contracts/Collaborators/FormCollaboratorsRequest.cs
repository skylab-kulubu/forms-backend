namespace Skylab.Forms.Application.Contracts.Collaborators;

public record FormCollaboratorsRequest(
    List<CollaboratorUpsertRequest> Collaborators
);
