namespace Skylab.Forms.Domain.Enums;

public enum WorkflowInstanceStatus
{
    Active = 0,
    Completed = 1,
    Terminated = 2,
    Faulted = 3,

    /// <summary>
    /// Ekip başvuruyu geçersiz saydı: kişi akışa baştan başlayabilir ve bu başvuru tek
    /// başvuru hakkından düşmez.
    /// </summary>
    Voided = 4
}
