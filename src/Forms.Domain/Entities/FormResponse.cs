using Forms.Domain.Enums;
using Forms.Domain.Models;

namespace Forms.Domain.Entities;

public class FormResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormId { get; set; }
    public Form Form { get; set; } = null!;
    public Guid? UserId { get; set; }
    public List<FormResponseSchemaItem> Data { get; set; } = new();
    public int? TimeSpent { get; set; }
    public FormResponseStatus Status { get; set; } = FormResponseStatus.Pending;
    public bool IsArchived { get; set; } = false;
    public Guid? ArchivedBy { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}