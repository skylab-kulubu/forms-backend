using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services;

public interface IFormMailNotifier
{
    Task NotifyResponseCopyAsync(Form form, FormResponse response, CancellationToken ct = default);
    /// <param name="nextFormId">Akış bu onaydan sonra bir forma yönlendiriyorsa o form; düzeltme istendiyse düzeltilecek form.</param>
    Task NotifyStatusChangedAsync(Form form, FormResponse response, Guid? nextFormId = null, CancellationToken ct = default);

    /// <summary>Ekip başvuruyu geçersiz saydı; kişi akışa baştan başlayabilir.</summary>
    Task NotifyRunReopenedAsync(WorkflowRunVoided voided, CancellationToken ct = default);

    bool CanNotifyAttempts { get; }

    Task NotifyAttemptAsync(Form form, Guid userId, string kind, DateTime? deadlineAt = null, int? minutes = null, Guid? nextFormId = null, CancellationToken ct = default);
}

public static class AttemptMailKind
{
    public const string Reminder = "reminder";
    public const string Extended = "extended";
    public const string Expired = "expired";
    public const string ExpiredEmpty = "expiredEmpty";
    public const string Accepted = "accepted";
    public const string Closed = "closed";
}
