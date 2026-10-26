using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Attempts;

/// <summary>Ekibin bir denemeye verdiği kararlar: ek süre, geçici cevabı kabul ya da kapatma, hatırlatma.</summary>
public interface IAttemptDecisions
{
    Task<ServiceResult<FormAttemptDetailContract>> ExtendAsync(Guid attemptId, Guid actorId, AttemptExtendRequest request, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptDetailContract>> AcceptAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptDetailContract>> CloseAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptDetailContract>> RemindAsync(Guid attemptId, Guid actorId, CancellationToken ct = default);
}

public sealed class AttemptDecisions(
    IFormAttemptRepository attempts,
    IFormResponseRepository responses,
    IFormDraftService drafts,
    IFormWorkflowRuntime workflowRuntime,
    IFormsUnitOfWork uow,
    IFormMailNotifier mail,
    ICurrentUser currentUser,
    ICacheService cache,
    IAttemptReadModels readModels,
    TimeProvider time) : IAttemptDecisions
{
    public async Task<ServiceResult<FormAttemptDetailContract>> ExtendAsync(Guid attemptId, Guid actorId, AttemptExtendRequest request, CancellationToken ct = default)
    {
        if (request.Minutes < 1 || request.Minutes > AttemptLimits.MaxExtensionMinutes)
            return Rejected($"Süre 1 dakika ile {TimeSpan.FromMinutes(AttemptLimits.MaxExtensionMinutes).TotalDays:0} gün arasında olmalıdır.");

        var note = NormalizeNote(request.Note);
        if (note?.Length > AttemptLimits.MaxNoteLength)
            return Rejected($"Not en fazla {AttemptLimits.MaxNoteLength} karakter olabilir.");

        var attempt = await attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        var now = time.GetUtcNow().UtcDateTime;
        var events = await attempts.GetEventsAsync(attempt.Id, ct);
        var closedByTeam = events.Any(item => item.Type == FormAttemptEventType.Closed);
        var snapshot = attempt.DraftSnapshot;
        var timeSpent = 0;
        List<FormResponseSchemaItem>? submission = null;

        if (!attempt.CanBeExtended(closedByTeam)) return Rejected("Bu adaya şu anda süre verilemez.");

        switch (attempt.Status)
        {
            case FormAttemptStatus.Started:
                var from = attempt.DeadlineAt is { } deadline && deadline > now ? deadline : now;
                attempt.DeadlineAt = from.AddMinutes(request.Minutes);
                snapshot = null;
                break;

            case FormAttemptStatus.Provisional:
                if (attempt.ResponseId is { } responseId)
                {
                    var provisional = await responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, ct);
                    if (provisional is { Status: FormResponseStatus.Provisional })
                    {
                        timeSpent = provisional.TimeSpent ?? 0;
                        submission = provisional.Data;
                        responses.Remove(provisional);
                    }
                }

                attempt.ResponseId = null;
                Reopen(attempt, now, request.Minutes);
                break;

            default:
                snapshot = null;
                Reopen(attempt, now, request.Minutes);
                break;
        }

        attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Extended,
            ActorUserId = actorId,
            Minutes = request.Minutes,
            Note = note,
            DeadlineAt = attempt.DeadlineAt,
            CreatedAt = now
        });

        // Geçici cevabın yanıtlarının tek kopyası artık bellekte: önce taslağa yazılır, commit en son gelir. Taslak
        // yazılamazsa uzatma kaydedilmez ve geçici cevap yerinde kalır.
        if (snapshot is { Count: > 0 })
            await drafts.RestoreResponseDraftAsync(attempt.FormId, attempt.UserId, snapshot, timeSpent, submission, ct);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await mail.NotifyAttemptAsync(attempt.Form, attempt.UserId, AttemptMailKind.Extended, attempt.DeadlineAt, request.Minutes, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await readModels.DetailOfAsync(attempt, attempt.Form, null, ct),
            $"Adaya {DurationText.Of(request.Minutes)} verildi.");
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> AcceptAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default)
    {
        var note = NormalizeNote(request?.Note);
        if (note?.Length > AttemptLimits.MaxNoteLength)
            return Rejected($"Not en fazla {AttemptLimits.MaxNoteLength} karakter olabilir.");

        var attempt = await attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        if (attempt.Status != FormAttemptStatus.Provisional || attempt.ResponseId is not { } responseId)
            return Rejected("Yalnızca geçici cevaplar teslim olarak kabul edilebilir.");

        var response = await responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, ct);
        if (response is not { Status: FormResponseStatus.Provisional })
            return Rejected("Geçici cevap bulunamadı.");

        var now = time.GetUtcNow().UtcDateTime;

        attempt.Status = FormAttemptStatus.Submitted;

        attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Accepted,
            ActorUserId = actorId,
            Note = note,
            CreatedAt = now
        });

        Guid? nextFormId = null;

        try
        {
            var routedInWorkflow = false;

            if (attempt.WorkflowStepId is not null)
            {
                var routed = await workflowRuntime.AcceptAsync(response.Form, response, attempt.UserId, ct);

                if (routed.Data is not { State: WorkflowActionState.NotInWorkflow })
                {
                    if (routed.Status.IsFailure() || routed.Data is null)
                        return new ServiceResult<FormAttemptDetailContract>(routed.Status, Message: routed.Message);

                    routedInWorkflow = true;
                    nextFormId = AttemptRouting.NextFormOf(routed);
                }
            }

            if (!routedInWorkflow)
                response.Status = response.Form.RequiresManualReview ? FormResponseStatus.Pending : FormResponseStatus.NonRestrict;

            await uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await cache.TryRemoveAsync(FormCacheKeys.Analytics(response.FormId), ct);
        await mail.NotifyResponseCopyAsync(response.Form, response, ct);
        await mail.NotifyAttemptAsync(response.Form, attempt.UserId, AttemptMailKind.Accepted, nextFormId: nextFormId, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await readModels.DetailOfAsync(attempt, response.Form, response, ct),
            "Geçici cevap teslim olarak kabul edildi.");
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> CloseAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default)
    {
        var note = NormalizeNote(request?.Note);
        if (note?.Length > AttemptLimits.MaxNoteLength)
            return Rejected($"Not en fazla {AttemptLimits.MaxNoteLength} karakter olabilir.");

        var attempt = await attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        if (attempt.Status != FormAttemptStatus.Provisional)
            return Rejected("Yalnızca geçici cevaplar teslim yok olarak kapatılabilir.");

        var now = time.GetUtcNow().UtcDateTime;

        if (attempt.ResponseId is { } responseId)
        {
            var provisional = await responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, ct);

            if (provisional is { Status: FormResponseStatus.Provisional }) provisional.Archive(actorId, now);
        }

        attempt.Status = FormAttemptStatus.NoSubmission;

        attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Closed,
            ActorUserId = actorId,
            Note = note,
            CreatedAt = now
        });

        Guid? nextFormId = null;

        try
        {
            if (attempt.WorkflowStepId is { } stepId)
                nextFormId = AttemptRouting.NextFormOf(await workflowRuntime.TimeOutAsync(stepId, ct));

            await uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await mail.NotifyAttemptAsync(attempt.Form, attempt.UserId, AttemptMailKind.Closed, nextFormId: nextFormId, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await readModels.DetailOfAsync(attempt, attempt.Form, null, ct),
            "Geçici cevap teslim yok olarak kapatıldı.");
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> RemindAsync(Guid attemptId, Guid actorId, CancellationToken ct = default)
    {
        var attempt = await attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        if (attempt.Status != FormAttemptStatus.Opened)
            return Rejected("Yalnızca görevi başlatmamış adaylara hatırlatma gönderilebilir.");

        if (!mail.CanNotifyAttempts)
            return Rejected("Hatırlatma e-postası için şablon tanımlı değil.");

        var now = time.GetUtcNow().UtcDateTime;

        if (attempt.Form.HasClosedAt(now))
            return Rejected("Son başlama saati geçti; hatırlatma gönderilemez.");

        if (attempt.ReminderSentAt is { } sentAt && now - sentAt < AttemptLimits.ReminderCooldown)
            return Rejected($"Bu adaya son {AttemptLimits.ReminderCooldown.TotalHours:0} saat içinde hatırlatma gönderildi.");

        attempt.ReminderSentAt = now;

        attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Reminded,
            ActorUserId = actorId,
            CreatedAt = now
        });

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await mail.NotifyAttemptAsync(attempt.Form, attempt.UserId, AttemptMailKind.Reminder, attempt.Form.ClosesAt, attempt.Form.TimeLimitMinutes, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await readModels.DetailOfAsync(attempt, attempt.Form, null, ct),
            "Hatırlatma gönderildi.");
    }

    private static void Reopen(FormAttempt attempt, DateTime now, int minutes)
    {
        attempt.Status = FormAttemptStatus.Started;
        attempt.ExpiredAt = null;
        attempt.DraftSnapshot = null;
        attempt.DeadlineAt = now.AddMinutes(minutes);
    }

    private bool CanManage(Form form, Guid userId) =>
        form.Allows(userId, currentUser.IsPlatformAdmin, FormAction.HandleResponses);

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static ServiceResult<FormAttemptDetailContract> Rejected(string message) =>
        new(ServiceStatus.NotAcceptable, Message: message);

    private static ServiceResult<FormAttemptDetailContract> NotFound() =>
        new(ServiceStatus.NotFound, Message: "Kayıt bulunamadı.");

    private static ServiceResult<FormAttemptDetailContract> NotAuthorized() =>
        new(ServiceStatus.NotAuthorized, Message: "Bu işlem için yetkiniz yok.");

    private static ServiceResult<FormAttemptDetailContract> Conflicted() =>
        new(ServiceStatus.Conflict, Message: "Kayıt bu sırada değişti; sayfayı yenileyip tekrar deneyin.");
}
