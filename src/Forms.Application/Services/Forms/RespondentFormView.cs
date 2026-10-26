using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Forms;

/// <summary>Formu dolduranın gördüğü: kapalı, akışta, süreli ya da daha önce cevaplanmış formun ekranı ve paylaşım kartı.</summary>
public interface IRespondentFormView
{
    Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormMetaContract>> GetFormMetaByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class RespondentFormView : IRespondentFormView
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormAttemptService _attempts;
    private readonly IGuestUploadService _guestUploads;
    private readonly TimeProvider _time;

    public RespondentFormView(
        IFormRepository forms,
        IFormResponseRepository responses,
        IFormWorkflowRuntime workflowRuntime,
        IFormAttemptService attempts,
        IGuestUploadService guestUploads,
        TimeProvider time)
    {
        _forms = forms;
        _responses = responses;
        _workflowRuntime = workflowRuntime;
        _attempts = attempts;
        _guestUploads = guestUploads;
        _time = time;
    }

    public async Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted)
            return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotFound);

        var now = _time.GetUtcNow().UtcDateTime;

        // Kapalı form "yok" değildir: istemci bunu ayrı bir ekranla karşılayabilsin.
        switch (form.AvailabilityAt(now))
        {
            case FormAvailability.Closed:
                return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotAvailable, new FormDisplayPayload(null, 0, FormTitle: form.Title), "Bu form şu anda yanıt kabul etmiyor.");
            case FormAvailability.ClosesAtPassed:
                return ClosesAtPassed(form, FormClosedReason.Closed, "Bu form kapanış saatinde kendiliğinden kapandı.");
        }

        if (userId == null && !form.AllowAnonymousResponses)
        {
            return new ServiceResult<FormDisplayPayload>(
                ServiceStatus.Unauthorized,
                new FormDisplayPayload(null, 0, FormTitle: form.Title),
                "Bu formu görüntülemek için giriş yapmalısınız."
            );
        }

        if (userId.HasValue && form.HasTimeLimit && await _attempts.FindSettledAsync(form, userId.Value, cancellationToken) is { } settled)
            return await TimedDisplayAsync(form, settled, now, cancellationToken);

        if (userId.HasValue)
        {
            var workflow = await _workflowRuntime.ResolveDisplayAsync(id, userId.Value, cancellationToken);

            // Veri yoksa hata vardır; ikisi de akış yoluna aittir.
            if (workflow.Data is not { State: WorkflowActionState.NotInWorkflow })
                return await MapWorkflowDisplayAsync(workflow, form.Title, userId.Value, now, cancellationToken);
        }

        if (userId.HasValue && form.HasTimeLimit)
        {
            var attempt = await _attempts.PrepareDisplayAsync(form, userId.Value, null, cancellationToken);

            if (attempt.Contract.State != FormAttemptState.Submitted)
                return await TimedDisplayAsync(form, attempt, now, cancellationToken);
        }

        var latestResponse = userId.HasValue
            ? await _responses.GetLatestForUserAsync(id, userId.Value, cancellationToken)
            : null;

        if (latestResponse is not null && !form.AllowMultipleResponses)
        {
            var answered = new FormDisplayPayload(null, 0, latestResponse.ReviewNote, latestResponse.ReviewedAt, FormTitle: form.Title, SubmittedAt: latestResponse.SubmittedAt);

            return latestResponse.Status switch
            {
                FormResponseStatus.Pending => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.PendingApproval, answered, "Form cevabınız inceleniyor, lütfen bekleyiniz."),
                FormResponseStatus.Approved => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Approved, answered, "Başvurunuz onaylanmıştır."),
                FormResponseStatus.Declined => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Declined, answered, "Başvurunuz reddedilmiştir."),
                _ => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Created, answered, "Bu formu daha önce doldurdunuz.")
            };
        }

        return new ServiceResult<FormDisplayPayload>(
            ServiceStatus.Success,
            MapToDisplayPayload(form, 0) with { ServerNow = now, ClosesAt = form.ClosesAt, GuestUploads = _guestUploads.CapabilityFor(form) });
    }

    private async Task<ServiceResult<FormDisplayPayload>> TimedDisplayAsync(
        Form form,
        AttemptDisplay attempt,
        DateTime now,
        CancellationToken cancellationToken,
        FormDisplayPayload? workflowPayload = null)
    {
        if (attempt.Contract.State == FormAttemptState.StartClosed)
            return ClosesAtPassed(form, FormClosedReason.StartClosed, "Son başlama saati geçti; görev artık başlatılamaz.");

        var payload = workflowPayload ?? MapToDisplayPayload(form, 0);
        var contract = attempt.Contract;
        var settled = contract.State is FormAttemptState.Provisional or FormAttemptState.NoSubmission;

        if (settled && attempt.WorkflowStepId is { } stepId)
        {
            var journey = await _workflowRuntime.GetJourneyByStepAsync(stepId, cancellationToken);
            if (journey is not null) payload = payload with { Workflow = journey, State = payload.State ?? WorkflowActionState.ShowForm };

            contract = contract with { NextFormId = await _workflowRuntime.GetNextFormByStepAsync(stepId, cancellationToken) };
        }

        if (contract.State != FormAttemptState.Running && payload.Form is { } shown)
        {
            contract = contract with
            {
                Deliverables = [.. shown.Schema
                    .Where(item => item.Type != "separator")
                    .Select(ResponseDataMapper.QuestionOf)
                    .Where(question => question.Length > 0)]
            };

            payload = payload with { Form = shown with { Schema = [], Task = null } };
        }

        return new ServiceResult<FormDisplayPayload>(
            ServiceStatus.Success,
            payload with { Attempt = contract, ServerNow = now, ClosesAt = form.ClosesAt, FormTitle = form.Title });
    }

    private ServiceResult<FormDisplayPayload> ClosesAtPassed(Form form, string reason, string message) =>
        new(ServiceStatus.NotAvailable,
            new FormDisplayPayload(null, 0, FormTitle: form.Title, Reason: reason, ClosesAt: form.ClosesAt, ServerNow: _time.GetUtcNow().UtcDateTime),
            message);

    public async Task<ServiceResult<FormMetaContract>> GetFormMetaByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted)
            return new ServiceResult<FormMetaContract>(ServiceStatus.NotFound);

        if (form.AvailabilityAt(_time.GetUtcNow().UtcDateTime) != FormAvailability.Open)
            return new ServiceResult<FormMetaContract>(ServiceStatus.NotAvailable, Message: "Bu form şu anda yanıt kabul etmiyor.");

        return new ServiceResult<FormMetaContract>(
            ServiceStatus.Success,
            Data: new FormMetaContract(form.Title, form.Description)
        );
    }

    private async Task<ServiceResult<FormDisplayPayload>> MapWorkflowDisplayAsync(
        ServiceResult<WorkflowStepOutcome> workflow,
        string formTitle,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (workflow.Data is not { } outcome)
            return new ServiceResult<FormDisplayPayload>(workflow.Status, Message: workflow.Message);

        FormDisplayContract? contract = null;
        Form? target = null;

        if (outcome is { State: WorkflowActionState.ShowForm, FormId: { } targetFormId })
        {
            target = await _forms.GetByIdAsync(targetFormId, cancellationToken);
            if (target == null) return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotFound);

            switch (target.AvailabilityAt(now))
            {
                case FormAvailability.Closed:
                    return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotAvailable, new FormDisplayPayload(null, 0, FormTitle: target.Title), "Bu form şu anda yanıt kabul etmiyor.");
                case FormAvailability.ClosesAtPassed:
                    return ClosesAtPassed(target, FormClosedReason.Closed, "Bu form kapanış saatinde kendiliğinden kapandı.");
            }

            var requiresManualReview = outcome.Journey?.Route.FirstOrDefault(step => step.Status == WorkflowJourneyStatus.Current)?.RequiresManualReview ?? false;
            contract = ToDisplayContract(target, requiresManualReview);
        }

        var payload = new FormDisplayPayload(
            contract,
            LegacyStep.From(outcome),
            outcome.ReviewNote,
            outcome.ReviewedAt,
            outcome.InstanceId,
            outcome.State,
            outcome.Stage,
            outcome.StartFormId,
            outcome.Reason,
            outcome.Journey,
            formTitle,
            LastRun: outcome.LastRun,
            ServerNow: now,
            ClosesAt: target?.ClosesAt,
            Revision: outcome.Revision);

        if (target is { HasTimeLimit: true })
        {
            var attempt = await _attempts.PrepareDisplayAsync(target, userId, outcome.StepId, cancellationToken);

            if (attempt.Contract.State != FormAttemptState.Submitted)
                return await TimedDisplayAsync(target, attempt, now, cancellationToken, payload);
        }

        return new ServiceResult<FormDisplayPayload>(workflow.Status, payload, workflow.Message);
    }

    private FormDisplayPayload MapToDisplayPayload(Form form, int step, string? reviewNote = null, DateTime? reviewedAt = null) =>
        new(ToDisplayContract(form, form.RequiresManualReview), step, reviewNote, reviewedAt);

    private static FormDisplayContract ToDisplayContract(Form form, bool requiresManualReview) =>
        new(
            form.Id,
            form.Title,
            form.Description,
            form.Schema,
            requiresManualReview,
            form.Task,
            form.ClosesAt,
            form.TimeLimitMinutes,
            form.AllowAnonymousResponses);
}
