using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Attempts;

public class FormAttemptService : IFormAttemptService
{
    private readonly IFormAttemptRepository _attempts;
    private readonly IFormRepository _forms;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormsUnitOfWork _uow;
    private readonly IAttemptExpiry _expiry;
    private readonly TimeProvider _time;

    public FormAttemptService(
        IFormAttemptRepository attempts,
        IFormRepository forms,
        IFormWorkflowRuntime workflowRuntime,
        IFormsUnitOfWork uow,
        IAttemptExpiry expiry,
        TimeProvider time)
    {
        _attempts = attempts;
        _forms = forms;
        _workflowRuntime = workflowRuntime;
        _uow = uow;
        _expiry = expiry;
        _time = time;
    }

    public async Task<AttemptDisplay> PrepareDisplayAsync(Form form, Guid userId, Guid? stepId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var attempt = await _attempts.GetLatestForEditAsync(form.Id, userId, ct);

        if (attempt is null)
        {
            if (form.HasClosedAt(now)) return new AttemptDisplay(StartClosedDisplay(form), stepId);

            attempt = await OpenAsync(form, userId, stepId, now, ct);
        }

        if (attempt.HasExpiredAt(now))
            attempt = await _expiry.ExpireAsync(attempt, form, ct) ?? await _attempts.GetLatestForEditAsync(form.Id, userId, ct) ?? attempt;

        return new AttemptDisplay(await DisplayOfAsync(form, attempt, now, ct), attempt.WorkflowStepId);
    }

    public async Task<AttemptDisplay?> FindSettledAsync(Form form, Guid userId, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetLatestAsync(form.Id, userId, ct);

        if (attempt is not { Status: FormAttemptStatus.Provisional or FormAttemptStatus.NoSubmission }) return null;

        return new AttemptDisplay(await DisplayOfAsync(form, attempt, _time.GetUtcNow().UtcDateTime, ct), attempt.WorkflowStepId);
    }

    public async Task<ServiceResult<FormAttemptStartResult>> StartAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        try
        {
            return await StartCoreAsync(formId, userId, ct);
        }
        catch (StorageConflictException)
        {
            return await StartCoreAsync(formId, userId, ct);
        }
    }

    private async Task<ServiceResult<FormAttemptStartResult>> StartCoreAsync(Guid formId, Guid userId, CancellationToken ct)
    {
        var form = await _forms.GetByIdAsync(formId, ct);
        if (form is null || form.Status == FormStatus.Deleted)
            return new ServiceResult<FormAttemptStartResult>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (form.Status != FormStatus.Open)
            return new ServiceResult<FormAttemptStartResult>(ServiceStatus.NotAvailable, Message: "Bu form şu anda yanıt kabul etmiyor.");

        if (!form.HasTimeLimit)
            return new ServiceResult<FormAttemptStartResult>(ServiceStatus.NotAcceptable, Message: "Bu formda kişisel süre yok.");

        var now = _time.GetUtcNow().UtcDateTime;
        var resolution = await _workflowRuntime.ResolveDisplayAsync(formId, userId, ct);
        var inWorkflow = resolution.Data is not { State: WorkflowActionState.NotInWorkflow };
        Guid? stepId = null;
        Guid? instanceId = null;

        if (inWorkflow)
        {
            if (resolution.Data is not { State: WorkflowActionState.ShowForm } shown || shown.FormId != formId)
            {
                return new ServiceResult<FormAttemptStartResult>(
                    resolution.Status.IsFailure() ? resolution.Status : ServiceStatus.NotAcceptable,
                    Message: resolution.Message ?? "Bu görevi şu anda başlatamazsınız.");
            }

            stepId = shown.StepId;
            instanceId = shown.InstanceId;
        }

        var attempt = await _attempts.GetLatestForEditAsync(formId, userId, ct);

        if (attempt is null)
        {
            if (form.HasClosedAt(now)) return StartClosedResult();

            attempt = await OpenAsync(form, userId, stepId, now, ct);
        }

        if (attempt.Status != FormAttemptStatus.Opened)
            return StartedResult(await DisplayOfAsync(form, attempt, now, ct), now, instanceId);

        if (form.HasClosedAt(now)) return StartClosedResult();

        if (inWorkflow && stepId is null)
        {
            var begun = await _workflowRuntime.BeginRunAsync(form, userId, ct);

            if (begun.Data is not { State: WorkflowActionState.ShowForm, StepId: { } begunStepId } run)
            {
                return new ServiceResult<FormAttemptStartResult>(
                    begun.Status.IsFailure() ? begun.Status : ServiceStatus.NotAcceptable,
                    Message: begun.Message ?? "Başvuru başlatılamadı.");
            }

            stepId = begunStepId;
            instanceId = run.InstanceId;
        }

        attempt.WorkflowStepId ??= stepId;
        attempt.Status = FormAttemptStatus.Started;
        attempt.StartedAt = now;
        attempt.DeadlineAt = now.AddMinutes(form.TimeLimitMinutes!.Value);

        _attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Started,
            DeadlineAt = attempt.DeadlineAt,
            CreatedAt = now
        });

        await _uow.SaveChangesAsync(ct);

        return StartedResult(await DisplayOfAsync(form, attempt, now, ct), now, instanceId);
    }

    public async Task<AttemptGate> CheckSubmitAsync(Form form, Guid userId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var attempt = await _attempts.GetLatestForEditAsync(form.Id, userId, ct);

        if (attempt is null || attempt.Status == FormAttemptStatus.Opened)
            return AttemptGate.Reject(Closed(FormClosedReason.NotStarted, "Görevi başlatmadan cevap gönderemezsiniz."));

        if (attempt.Status == FormAttemptStatus.Submitted)
            return AttemptGate.Reject(new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz."));

        if (attempt.AcceptsSubmissionAt(now)) return AttemptGate.Allow(attempt);

        if (attempt.HasExpiredAt(now)) await _expiry.ExpireAsync(attempt, form, ct);

        return AttemptGate.Reject(Closed(FormClosedReason.TimeUp, "Süren doldu."));
    }

    public void MarkSubmitted(FormAttempt attempt, Guid responseId)
    {
        attempt.Status = FormAttemptStatus.Submitted;
        attempt.ResponseId = responseId;
        attempt.DraftSnapshot = null;

        _attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.Submitted, CreatedAt = _time.GetUtcNow().UtcDateTime });
    }

    private async Task<FormAttempt> OpenAsync(Form form, Guid userId, Guid? stepId, DateTime now, CancellationToken ct)
    {
        var attempt = new FormAttempt
        {
            FormId = form.Id,
            UserId = userId,
            WorkflowStepId = stepId,
            Status = FormAttemptStatus.Opened
        };

        attempt.Events.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.Opened, CreatedAt = now });
        _attempts.Add(attempt);

        try
        {
            await _uow.SaveChangesAsync(ct);
            return attempt;
        }
        catch (StorageConflictException)
        {
            var existing = await _attempts.GetLatestForEditAsync(form.Id, userId, ct);
            if (existing is null) throw;

            return existing;
        }
    }

    private async Task<FormAttemptDisplayContract> DisplayOfAsync(Form form, FormAttempt attempt, DateTime now, CancellationToken ct)
    {
        IReadOnlyList<FormAttemptEvent> events = attempt.Status == FormAttemptStatus.Opened
            ? []
            : await _attempts.GetEventsAsync(attempt.Id, ct);

        var extensions = events.Where(item => item.Type == FormAttemptEventType.Extended).ToList();
        var last = extensions.OrderByDescending(item => item.CreatedAt).FirstOrDefault();

        var state = attempt.Status switch
        {
            FormAttemptStatus.Opened => form.HasClosedAt(now) ? FormAttemptState.StartClosed : FormAttemptState.NotStarted,
            FormAttemptStatus.Started => FormAttemptState.Running,
            FormAttemptStatus.Provisional => FormAttemptState.Provisional,
            FormAttemptStatus.NoSubmission => FormAttemptState.NoSubmission,
            _ => FormAttemptState.Submitted
        };

        return new FormAttemptDisplayContract(
            state,
            form.TimeLimitMinutes ?? 0,
            form.ClosesAt,
            attempt.StartedAt,
            attempt.DeadlineAt,
            extensions.Sum(item => item.Minutes ?? 0),
            last is null ? null : new FormAttemptExtensionContract(last.Minutes ?? 0, last.CreatedAt),
            events.Any(item => item.Type == FormAttemptEventType.Closed),
            events.Any(item => item.Type == FormAttemptEventType.Expired));
    }

    private static FormAttemptDisplayContract StartClosedDisplay(Form form) =>
        new(FormAttemptState.StartClosed, form.TimeLimitMinutes ?? 0, form.ClosesAt, null, null, 0, null, false, false);

    private static ServiceResult<FormAttemptStartResult> StartedResult(FormAttemptDisplayContract display, DateTime now, Guid? instanceId) =>
        new(ServiceStatus.Success, new FormAttemptStartResult(display, now, instanceId));

    private static ServiceResult<FormAttemptStartResult> StartClosedResult() =>
        new(ServiceStatus.NotAvailable, Message: "Son başlama saati geçti; görev artık başlatılamaz.");

    private static ServiceResult<ResponseSubmitResult> Closed(string reason, string message) =>
        new(ServiceStatus.NotAvailable, new ResponseSubmitResult(null, null, 0, Reason: reason), message);
}
