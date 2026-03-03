using Skylab.Forms.Domain.Models;
using Skylab.Shared.Domain.Entities;

namespace Skylab.Forms.Domain.Entities;

public class ComponentGroup : BaseEntity
{
    public string Title { get; set; } = "Yeni Grup";
    public string? Description { get; set; }
    public List<FormSchemaItem> Schema { get; set; } = new();
    public Guid OwnedBy { get; set; }
} 