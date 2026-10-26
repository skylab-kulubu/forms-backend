using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

/// <summary>
/// Tek bir kullanıcının akışı bir kez çalıştırması. Seçilen rota adımlara
/// yazıldığı için sonradan yeniden hesaplanmaz.
/// </summary>
public class FormWorkflowInstance : BaseEntity
{
    /// <summary>WorkflowInstances.VoidNote kolonunun uzunluğu (varchar(500)).</summary>
    public const int VoidNoteMaxLength = 500;

    public Guid WorkflowId { get; set; }
    public FormWorkflow Workflow { get; set; } = null!;

    public Guid WorkflowVersionId { get; set; }
    public FormWorkflowVersion WorkflowVersion { get; set; } = null!;

    public Guid UserId { get; set; }

    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Active;
    public WorkflowInstanceOutcome Outcome { get; set; } = WorkflowInstanceOutcome.None;

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public DateTime? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }

    /// <summary>Başvuruyu geçersiz sayan ekibin adaya notu.</summary>
    public string? VoidNote { get; set; }

    public ICollection<FormWorkflowStep> Steps { get; set; } = new List<FormWorkflowStep>();

    /// <summary>
    /// Adımlara yazan her işlem başvuruyu da değiştirmiş sayar. EF yalnız güncellediği satırın sürümünü denetler;
    /// başvuru satırı değişmezse aynı adıma eşzamanlı iki gönderim de commit eder.
    /// </summary>
    public void Touch(DateTime now) => UpdatedAt = now;

    /// <summary>Başvuru son adımında sonuçlandı; ret ve süre dolumu onu Terminated bitirir.</summary>
    public void Finish(WorkflowInstanceOutcome outcome, DateTime now)
    {
        Status = outcome is WorkflowInstanceOutcome.Declined or WorkflowInstanceOutcome.TimedOut
            ? WorkflowInstanceStatus.Terminated
            : WorkflowInstanceStatus.Completed;
        Outcome = outcome;
        CompletedAt = now;
    }

    /// <summary>
    /// Başvuru akış bitmeden durdu (akış arşivlendi, kişi silindi); bekleyen bir karar verildiyse sonucu kalır. Açık adımları
    /// çağıran kapatır. WorkflowRunStop aynı geçişi toplu yazar.
    /// </summary>
    public void Stop(DateTime now, WorkflowInstanceOutcome outcome = WorkflowInstanceOutcome.None)
    {
        Status = WorkflowInstanceStatus.Terminated;
        Outcome = outcome;
        CompletedAt = now;
    }

    /// <summary>Yayınlanmış tanım bu adım için rota üretemedi.</summary>
    public void Fault(DateTime now)
    {
        Status = WorkflowInstanceStatus.Faulted;
        CompletedAt = now;
    }

    /// <summary>
    /// Ekip başvuruyu geçersiz saydı: açık adımlar kapanır (adımlar yüklü olmalı). Sonuçlanmış başvurunun bitiş zamanı
    /// korunur. Geçersiz sayılan başvuru aktif miydi, onu döner.
    /// </summary>
    public bool Void(Guid actorId, string? note, DateTime now)
    {
        var wasActive = Status == WorkflowInstanceStatus.Active;

        foreach (var step in Steps.Where(step => step.CompletedAt is null))
            step.CompletedAt = now;

        Status = WorkflowInstanceStatus.Voided;
        VoidedAt = now;
        VoidedBy = actorId;
        VoidNote = note;
        if (wasActive) CompletedAt = now;

        return wasActive;
    }
}
