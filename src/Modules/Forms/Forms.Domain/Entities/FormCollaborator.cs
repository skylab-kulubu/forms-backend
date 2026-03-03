using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

public class FormCollaborator
{
    public Guid UserId { get; set; }
    public CollaboratorRole Role { get; set; } = CollaboratorRole.Viewer;
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
}