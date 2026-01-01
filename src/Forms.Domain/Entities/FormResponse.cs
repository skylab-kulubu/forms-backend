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
    public FormResponseStatus Status { get; set; } = FormResponseStatus.Pending;
    public Guid? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}