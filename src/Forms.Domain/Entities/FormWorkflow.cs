using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

public class FormWorkflow : BaseEntity
{
    public string Name { get; set; } = "Yeni Akış";
    public string? Description { get; set; }
    public Guid OwnerUserId { get; set; }
    public WorkflowStatus Status { get; set; } = WorkflowStatus.Draft;

    /// <summary>
    /// Formlardaki AllowMultipleResponses'ın akış karşılığı: bir kullanıcının
    /// akışı baştan sona birden fazla kez çalıştırabilmesi.
    /// </summary>
    public bool AllowMultipleRuns { get; set; } = false;

    /// <summary>
    /// Sürümde değil akışta tutulur: kapatma yayın beklemeden etkili olur ve eski
    /// sürümlere bağlı başvurulara da ulaşır. Arşivleme bunu kalıcı olarak Closed yapar.
    /// </summary>
    public WorkflowIntake Intake { get; set; } = WorkflowIntake.Open;

    public ICollection<FormWorkflowVersion> Versions { get; set; } = new List<FormWorkflowVersion>();
}
