using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Domain.Entities;

public class FormWorkflowStep : BaseEntity
{
    public Guid WorkflowInstanceId { get; set; }
    public FormWorkflowInstance WorkflowInstance { get; set; } = null!;

    public Guid NodeId { get; set; }
    public FormWorkflowNode Node { get; set; } = null!;

    /// <summary>Adım açıldığında boştur; kullanıcı formu gönderince dolar.</summary>
    public Guid? ResponseId { get; set; }
    public FormResponse? Response { get; set; }

    public Guid? PreviousStepId { get; set; }
    public FormWorkflowStep? PreviousStep { get; set; }

    /// <summary>Bu adımdan çıkarken seçilen rota. Karar anında yazılır.</summary>
    public Guid? SelectedTransitionId { get; set; }
    public FormWorkflowTransition? SelectedTransition { get; set; }

    /// <summary>1'den başlayan sıra numarası; üst sınırı publish doğrulaması belirler.</summary>
    public int Sequence { get; set; } = 1;

    /// <summary>Boş olan adım, başvurunun o an beklediği adımdır.</summary>
    public DateTime? CompletedAt { get; set; }
}
