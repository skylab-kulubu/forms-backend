using Skylab.Forms.Domain.Models;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Domain.Entities;

public class ComponentGroup : BaseEntity
{
    /// <summary>ComponentGroup.Title kolonunun uzunluğu (varchar(100)).</summary>
    public const int TitleMaxLength = 100;

    public string Title { get; set; } = "Yeni Şablon";
    public string? Description { get; set; }
    public List<FormSchemaItem> Schema { get; set; } = new();
    public Guid OwnedBy { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public Guid? ArchivedBy { get; set; }
} 
