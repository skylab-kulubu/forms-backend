using Forms.Domain.Enums;
using Forms.Domain.Models;

namespace Forms.Domain.Entities;

public class Form : BaseEntity
{
    public string Title { get; set; } = "Yeni Form";
    public string? Description { get; set; }
    public List<FormSchemaItem> Schema { get; set; } = new();
    public FormStatus Status { get; set; } = FormStatus.Open;
    public bool AllowAnonymousResponses { get; set; } = false;
    public bool AllowMultipleResponses { get; set; } = false;

    // Relations
    public Guid? LinkedFormId { get; set; }
    public Form? LinkedForm { get; set; }

    // Navigation
    public ICollection<FormCollaborator> Collaborators { get; set; } = new List<FormCollaborator>();
    public ICollection<FormResponse> Responses { get; set; } = new List<FormResponse>();
}