using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Attempts;

public interface IAttemptExpiry
{
    Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int take, CancellationToken ct = default);
    Task ExpireByIdAsync(Guid attemptId, CancellationToken ct = default);

    /// <summary>
    /// Süresi dolmuş denemeyi kapatır: taslağı varsa geçici cevaba çevirir, yoksa teslimsiz kapatıp akışı yönlendirir.
    /// Kayıt başka bir işlemle çakışırsa null döner; çağıran güncel denemeyi yeniden okur.
    /// </summary>
    Task<FormAttempt?> ExpireAsync(FormAttempt attempt, Form form, CancellationToken ct = default);
}

public sealed class AttemptExpiry(
    IFormAttemptRepository attempts,
    IFormResponseRepository responses,
    IFormWorkflowInstanceRepository instances,
    IFormDraftService drafts,
    IFormWorkflowRuntime workflowRuntime,
    IFormsUnitOfWork uow,
    IFormMailNotifier mail,
    TimeProvider time) : IAttemptExpiry
{
    public Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int take, CancellationToken ct = default) =>
        attempts.GetDueAsync(now - AttemptLimits.SubmitGrace, take, ct);

    public async Task ExpireByIdAsync(Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await attempts.GetForEditAsync(attemptId, ct);

        if (attempt is null || !attempt.HasExpiredAt(time.GetUtcNow().UtcDateTime) || !attempt.Form.HasTimeLimit) return;

        await ExpireAsync(attempt, attempt.Form, ct);
    }

    public async Task<FormAttempt?> ExpireAsync(FormAttempt attempt, Form form, CancellationToken ct = default)
    {
        if (attempt.Status != FormAttemptStatus.Started || attempt.DeadlineAt is not { } deadline) return attempt;

        // Başvuru durdurulduysa (akış arşivlendi) geçici cevap yönlendirilemez; deneme sessizce kapanır.
        if (attempt.WorkflowStepId is { } stepOfRun && !await instances.IsStepOpenAsync(stepOfRun, ct))
            return await CloseStoppedAsync(attempt, deadline, ct);

        var draft = (await drafts.GetResponseDraftAsync(form.Id, attempt.UserId, ct)).Data;
        var hasDraft = draft is { Responses.Count: > 0 };

        attempt.ExpiredAt = deadline;

        if (hasDraft)
        {
            var answers = draft!.Submission is { Count: > 0 } submission ? submission : ResponseDataMapper.FromDraft(draft.Responses);

            var response = new FormResponse
            {
                FormId = form.Id,
                UserId = attempt.UserId,
                Data = ResponseDataMapper.Map(form.Schema, answers),
                TimeSpent = draft.TimeSpent,
                Status = FormResponseStatus.Provisional,
                SubmittedAt = deadline
            };

            responses.Add(response);

            attempt.Status = FormAttemptStatus.Provisional;
            attempt.ResponseId = response.Id;
            attempt.DraftSnapshot = draft.Responses;

            attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.Expired, CreatedAt = deadline });
        }
        else
        {
            attempt.Status = FormAttemptStatus.NoSubmission;

            attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.ExpiredEmpty, CreatedAt = deadline });
        }

        Guid? nextFormId = null;

        try
        {
            if (!hasDraft && attempt.WorkflowStepId is { } stepId)
                nextFormId = AttemptRouting.NextFormOf(await workflowRuntime.TimeOutAsync(stepId, ct));

            await uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return null;
        }

        await mail.NotifyAttemptAsync(form, attempt.UserId, hasDraft ? AttemptMailKind.Expired : AttemptMailKind.ExpiredEmpty, deadline, nextFormId: nextFormId, ct: ct);

        return attempt;
    }

    private async Task<FormAttempt?> CloseStoppedAsync(FormAttempt attempt, DateTime deadline, CancellationToken ct)
    {
        attempt.Status = FormAttemptStatus.NoSubmission;
        attempt.ExpiredAt = deadline;
        // Closed olayı "ekip kapattı" demektir; burada kimse kapatmadı, süre teslimsiz doldu.
        attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.ExpiredEmpty, CreatedAt = deadline });

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return null;
        }

        return attempt;
    }
}
