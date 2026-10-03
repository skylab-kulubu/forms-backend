using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Attempts;

public class FormAttemptService : IFormAttemptService
{
    private readonly IFormAttemptRepository _attempts;
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly IFormDraftService _drafts;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormsUnitOfWork _uow;
    private readonly IFormMailNotifier _mail;
    private readonly IExternalUserService _users;
    private readonly ICurrentUserService _currentUser;
    private readonly ICacheService _cache;

    public FormAttemptService(
        IFormAttemptRepository attempts,
        IFormRepository forms,
        IFormResponseRepository responses,
        IFormWorkflowInstanceRepository instances,
        IFormDraftService drafts,
        IFormWorkflowRuntime workflowRuntime,
        IFormsUnitOfWork uow,
        IFormMailNotifier mail,
        IExternalUserService users,
        ICurrentUserService currentUser,
        ICacheService cache)
    {
        _attempts = attempts;
        _forms = forms;
        _responses = responses;
        _instances = instances;
        _drafts = drafts;
        _workflowRuntime = workflowRuntime;
        _uow = uow;
        _mail = mail;
        _users = users;
        _currentUser = currentUser;
        _cache = cache;
    }

    public async Task<AttemptDisplay> PrepareDisplayAsync(Form form, Guid userId, Guid? stepId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var attempt = await _attempts.GetLatestForEditAsync(form.Id, userId, ct);

        if (attempt is null)
        {
            if (form.HasClosedAt(now)) return new AttemptDisplay(StartClosedDisplay(form), stepId);

            attempt = await OpenAsync(form, userId, stepId, now, ct);
        }

        if (attempt.Status == FormAttemptStatus.Started && attempt.DeadlineAt <= now)
            attempt = await ExpireAsync(attempt, form, ct) ?? await _attempts.GetLatestForEditAsync(form.Id, userId, ct) ?? attempt;

        return new AttemptDisplay(await DisplayOfAsync(form, attempt, now, ct), attempt.WorkflowStepId);
    }

    public async Task<AttemptDisplay?> FindSettledAsync(Form form, Guid userId, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetLatestAsync(form.Id, userId, ct);

        if (attempt is not { Status: FormAttemptStatus.Provisional or FormAttemptStatus.NoSubmission }) return null;

        return new AttemptDisplay(await DisplayOfAsync(form, attempt, DateTime.UtcNow, ct), attempt.WorkflowStepId);
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

        var now = DateTime.UtcNow;
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
        var now = DateTime.UtcNow;
        var attempt = await _attempts.GetLatestForEditAsync(form.Id, userId, ct);

        if (attempt is null || attempt.Status == FormAttemptStatus.Opened)
            return AttemptGate.Reject(Closed(FormClosedReason.NotStarted, "Görevi başlatmadan cevap gönderemezsiniz."));

        if (attempt.Status == FormAttemptStatus.Submitted)
            return AttemptGate.Reject(new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz."));

        if (attempt.AcceptsSubmissionAt(now, AttemptLimits.SubmitGrace)) return AttemptGate.Allow(attempt);

        if (attempt.Status == FormAttemptStatus.Started) await ExpireAsync(attempt, form, ct);

        return AttemptGate.Reject(Closed(FormClosedReason.TimeUp, "Süren doldu."));
    }

    public async Task MarkSubmittedAsync(Guid attemptId, Guid responseId, CancellationToken ct = default)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var attempt = await _attempts.GetForEditAsync(attemptId, ct);
            if (attempt is null) return;

            if (attempt.Status == FormAttemptStatus.Provisional && attempt.ResponseId is { } provisionalId && provisionalId != responseId)
            {
                var provisional = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(provisionalId, ct);
                if (provisional is { Status: FormResponseStatus.Provisional }) _responses.Remove(provisional);
            }

            attempt.Status = FormAttemptStatus.Submitted;
            attempt.ResponseId = responseId;
            attempt.DraftSnapshot = null;

            _attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.Submitted });

            try
            {
                await _uow.SaveChangesAsync(ct);
                return;
            }
            catch (StorageConflictException) when (pass == 0)
            {
            }
        }
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> ExtendAsync(Guid attemptId, Guid actorId, AttemptExtendRequest request, CancellationToken ct = default)
    {
        if (request.Minutes < 1 || request.Minutes > AttemptLimits.MaxExtensionMinutes)
            return Rejected("Süre 1 dakika ile 7 gün arasında olmalıdır.");

        var note = NormalizeNote(request.Note);
        if (note?.Length > AttemptLimits.MaxNoteLength)
            return Rejected($"Not en fazla {AttemptLimits.MaxNoteLength} karakter olabilir.");

        var attempt = await _attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        var now = DateTime.UtcNow;
        var events = await _attempts.GetEventsAsync(attempt.Id, ct);
        var closedByTeam = events.Any(item => item.Type == FormAttemptEventType.Closed);
        var snapshot = attempt.DraftSnapshot;
        var timeSpent = 0;
        List<FormResponseSchemaItem>? submission = null;

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
                    var provisional = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, ct);
                    if (provisional is { Status: FormResponseStatus.Provisional })
                    {
                        timeSpent = provisional.TimeSpent ?? 0;
                        submission = provisional.Data;
                        _responses.Remove(provisional);
                    }
                }

                attempt.ResponseId = null;
                Reopen(attempt, now, request.Minutes);
                break;

            case FormAttemptStatus.NoSubmission when attempt.WorkflowStepId is null && !closedByTeam:
                snapshot = null;
                Reopen(attempt, now, request.Minutes);
                break;

            default:
                return Rejected("Bu adaya şu anda süre verilemez.");
        }

        _attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Extended,
            ActorUserId = actorId,
            Minutes = request.Minutes,
            Note = note,
            DeadlineAt = attempt.DeadlineAt,
            CreatedAt = now
        });

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        if (snapshot is { Count: > 0 })
            await _drafts.RestoreResponseDraftAsync(attempt.FormId, attempt.UserId, snapshot, timeSpent, submission, ct);

        await _mail.NotifyAttemptAsync(attempt.Form, attempt.UserId, AttemptMailKind.Extended, attempt.DeadlineAt, request.Minutes, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await DetailOfAsync(attempt, attempt.Form, null, ct),
            $"Adaya {DurationText(request.Minutes)} verildi.");
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> AcceptAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default)
    {
        var note = NormalizeNote(request?.Note);
        if (note?.Length > AttemptLimits.MaxNoteLength)
            return Rejected($"Not en fazla {AttemptLimits.MaxNoteLength} karakter olabilir.");

        var attempt = await _attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        if (attempt.Status != FormAttemptStatus.Provisional || attempt.ResponseId is not { } responseId)
            return Rejected("Yalnızca geçici cevaplar teslim olarak kabul edilebilir.");

        var response = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, ct);
        if (response is not { Status: FormResponseStatus.Provisional })
            return Rejected("Geçici cevap bulunamadı.");

        var now = DateTime.UtcNow;

        attempt.Status = FormAttemptStatus.Submitted;

        _attempts.Add(new FormAttemptEvent
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
                var routed = await _workflowRuntime.AcceptAsync(response.Form, response, attempt.UserId, ct);

                if (routed.Data is not { State: WorkflowActionState.NotInWorkflow })
                {
                    if (routed.Status.IsFailure() || routed.Data is null)
                        return new ServiceResult<FormAttemptDetailContract>(routed.Status, Message: routed.Message);

                    routedInWorkflow = true;
                    if (routed.Data is { State: WorkflowActionState.ShowForm, FormId: { } next }) nextFormId = next;
                }
            }

            if (!routedInWorkflow)
                response.Status = response.Form.RequiresManualReview ? FormResponseStatus.Pending : FormResponseStatus.NonRestrict;

            await _uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await _cache.TryRemoveAsync(FormCacheKeys.Analytics(response.FormId), ct);
        await _mail.NotifyResponseCopyAsync(response.Form, response, ct);
        await _mail.NotifyAttemptAsync(response.Form, attempt.UserId, AttemptMailKind.Accepted, nextFormId: nextFormId, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await DetailOfAsync(attempt, response.Form, response, ct),
            "Geçici cevap teslim olarak kabul edildi.");
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> CloseAsync(Guid attemptId, Guid actorId, AttemptDecisionRequest? request, CancellationToken ct = default)
    {
        var note = NormalizeNote(request?.Note);
        if (note?.Length > AttemptLimits.MaxNoteLength)
            return Rejected($"Not en fazla {AttemptLimits.MaxNoteLength} karakter olabilir.");

        var attempt = await _attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        if (attempt.Status != FormAttemptStatus.Provisional)
            return Rejected("Yalnızca geçici cevaplar teslim yok olarak kapatılabilir.");

        var now = DateTime.UtcNow;

        if (attempt.ResponseId is { } responseId)
        {
            var provisional = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, ct);

            if (provisional is { Status: FormResponseStatus.Provisional })
            {
                provisional.IsArchived = true;
                provisional.ArchivedBy = actorId;
                provisional.ArchivedAt = now;
            }
        }

        attempt.Status = FormAttemptStatus.NoSubmission;

        _attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Closed,
            ActorUserId = actorId,
            Note = note,
            CreatedAt = now
        });

        try
        {
            if (attempt.WorkflowStepId is { } stepId)
                await _workflowRuntime.TimeOutAsync(stepId, ct);

            await _uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await _mail.NotifyAttemptAsync(attempt.Form, attempt.UserId, AttemptMailKind.Closed, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await DetailOfAsync(attempt, attempt.Form, null, ct),
            "Geçici cevap teslim yok olarak kapatıldı.");
    }

    public async Task<ServiceResult<FormAttemptDetailContract>> RemindAsync(Guid attemptId, Guid actorId, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetForEditAsync(attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Form, actorId)) return NotAuthorized();

        if (attempt.Status != FormAttemptStatus.Opened)
            return Rejected("Yalnızca görevi başlatmamış adaylara hatırlatma gönderilebilir.");

        if (!_mail.CanNotifyAttempts)
            return Rejected("Hatırlatma e-postası için şablon tanımlı değil.");

        var now = DateTime.UtcNow;

        if (attempt.Form.HasClosedAt(now))
            return Rejected("Son başlama saati geçti; hatırlatma gönderilemez.");

        if (attempt.ReminderSentAt is { } sentAt && now - sentAt < AttemptLimits.ReminderCooldown)
            return Rejected("Bu adaya son 12 saat içinde hatırlatma gönderildi.");

        attempt.ReminderSentAt = now;

        _attempts.Add(new FormAttemptEvent
        {
            AttemptId = attempt.Id,
            Type = FormAttemptEventType.Reminded,
            ActorUserId = actorId,
            CreatedAt = now
        });

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Conflicted();
        }

        await _mail.NotifyAttemptAsync(attempt.Form, attempt.UserId, AttemptMailKind.Reminder, attempt.Form.ClosesAt, ct: ct);

        return new ServiceResult<FormAttemptDetailContract>(
            ServiceStatus.Success,
            await DetailOfAsync(attempt, attempt.Form, null, ct),
            "Hatırlatma gönderildi.");
    }

    public async Task<ServiceResult<FormAttemptViewContract>> GetViewAsync(Guid attemptId, Guid userId, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetWithFormAsync(attemptId, ct);
        if (attempt is null)
            return new ServiceResult<FormAttemptViewContract>(ServiceStatus.NotFound, Message: "Kayıt bulunamadı.");

        var isCollaborator = attempt.Form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        if (!isCollaborator && !await _currentUser.HasRoleAsync("skyforms:*", "forms", ct))
            return new ServiceResult<FormAttemptViewContract>(ServiceStatus.NotAuthorized, Message: "Bu kaydı görüntüleme yetkiniz yok.");

        var user = await _users.GetUserAsync(attempt.UserId, ct) ?? new UserContract(attempt.UserId, null, "??", null);
        var detail = await DetailOfAsync(attempt, attempt.Form, null, ct);
        var workflow = attempt.WorkflowStepId is { } stepId ? await StepWorkflowAsync(stepId, ct) : null;

        return new ServiceResult<FormAttemptViewContract>(
            ServiceStatus.Success,
            new FormAttemptViewContract(attempt.Id, attempt.FormId, user, detail, attempt.Form.Task, workflow));
    }

    public async Task<FormAttemptDetailContract?> GetDetailForResponseAsync(FormResponse response, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetByResponseAsync(response.Id, ct);

        return attempt is null ? null : await DetailOfAsync(attempt, response.Form, response, ct);
    }

    public async Task<Guid?> FindStepIdForResponseAsync(Guid responseId, CancellationToken ct = default) =>
        (await _attempts.GetByResponseAsync(responseId, ct))?.WorkflowStepId;

    public async Task<ServiceResult<FormAttemptAnalyticsContract>> GetAnalyticsAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        var form = await _forms.GetByIdAsync(formId, ct);
        if (form is null)
            return new ServiceResult<FormAttemptAnalyticsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!await _currentUser.HasRoleAsync("skyforms:*", "forms", ct) && !await _forms.IsUserCollaboratorAsync(formId, userId, ct))
            return new ServiceResult<FormAttemptAnalyticsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun analitiğini görüntüleme yetkiniz yok.");

        if (!form.HasTimeLimit)
            return new ServiceResult<FormAttemptAnalyticsContract>(ServiceStatus.NotAcceptable, Message: "Bu formda kişisel süre yok.");

        var stats = await _attempts.GetStatsAsync(formId, ct);

        var durations = stats.Durations
            .Select(item => (int)Math.Round((item.SubmittedAt - item.StartedAt).TotalMinutes))
            .Where(minutes => minutes >= 0)
            .OrderBy(minutes => minutes)
            .ToList();

        var byActor = stats.Extensions
            .Where(item => item.ActorUserId.HasValue)
            .GroupBy(item => item.ActorUserId!.Value)
            .Select(group => new { ActorId = group.Key, Count = group.Count(), Minutes = group.Sum(item => item.Minutes) })
            .OrderByDescending(item => item.Minutes)
            .ToList();

        var actors = byActor.Count == 0 ? [] : await _users.GetUsersAsync(byActor.Select(item => item.ActorId), ct);

        var extendedBy = byActor
            .Select(item => new FormAttemptActorStatContract(
                actors.FirstOrDefault(user => user.Id == item.ActorId) ?? new UserContract(item.ActorId, null, null, null),
                item.Count,
                item.Minutes))
            .ToList();

        return new ServiceResult<FormAttemptAnalyticsContract>(
            ServiceStatus.Success,
            new FormAttemptAnalyticsContract(
                form.TimeLimitMinutes ?? 0,
                stats.Opened,
                stats.Started,
                stats.Submitted,
                stats.Provisional,
                stats.Running,
                stats.NoSubmission,
                durations,
                stats.Extensions.Select(item => item.AttemptId).Distinct().Count(),
                stats.Extensions.Sum(item => item.Minutes),
                extendedBy,
                FindStalledQuestion(form, stats.ExpiredDrafts),
                stats.ExpiredWithDraft));
    }

    public Task<IReadOnlyList<Guid>> GetDueAsync(DateTime now, int take, CancellationToken ct = default) =>
        _attempts.GetDueAsync(now - AttemptLimits.SubmitGrace, take, ct);

    public async Task ExpireByIdAsync(Guid attemptId, CancellationToken ct = default)
    {
        var attempt = await _attempts.GetForEditAsync(attemptId, ct);

        if (attempt is not { Status: FormAttemptStatus.Started, DeadlineAt: { } deadline }) return;
        if (deadline > DateTime.UtcNow - AttemptLimits.SubmitGrace) return;
        if (!attempt.Form.HasTimeLimit) return;

        await ExpireAsync(attempt, attempt.Form, ct);
    }

    private async Task<FormAttempt?> ExpireAsync(FormAttempt attempt, Form form, CancellationToken ct)
    {
        if (attempt.Status != FormAttemptStatus.Started || attempt.DeadlineAt is not { } deadline) return attempt;

        var draft = (await _drafts.GetResponseDraftAsync(form.Id, attempt.UserId, ct)).Data;
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

            _responses.Add(response);

            attempt.Status = FormAttemptStatus.Provisional;
            attempt.ResponseId = response.Id;
            attempt.DraftSnapshot = draft.Responses;

            _attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.Expired, CreatedAt = deadline });
        }
        else
        {
            attempt.Status = FormAttemptStatus.NoSubmission;

            _attempts.Add(new FormAttemptEvent { AttemptId = attempt.Id, Type = FormAttemptEventType.ExpiredEmpty, CreatedAt = deadline });
        }

        try
        {
            if (!hasDraft && attempt.WorkflowStepId is { } stepId)
                await _workflowRuntime.TimeOutAsync(stepId, ct);

            await _uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return null;
        }

        await _mail.NotifyAttemptAsync(form, attempt.UserId, hasDraft ? AttemptMailKind.Expired : AttemptMailKind.ExpiredEmpty, deadline, ct: ct);

        return attempt;
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

    private async Task<FormAttemptDetailContract> DetailOfAsync(FormAttempt attempt, Form form, FormResponse? response, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var events = await _attempts.GetEventsAsync(attempt.Id, ct);

        var actorIds = events
            .Where(item => item.ActorUserId.HasValue)
            .Select(item => item.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var actors = actorIds.Count == 0 ? [] : await _users.GetUsersAsync(actorIds, ct);
        var closedByTeam = events.Any(item => item.Type == FormAttemptEventType.Closed);

        var acceptRequiresReview = form.RequiresManualReview;
        FormAttemptRouteContract? onAccept = null;
        FormAttemptRouteContract? onClose = null;

        if (attempt.Status == FormAttemptStatus.Provisional && attempt.WorkflowStepId is { } stepId)
        {
            var preview = await _workflowRuntime.PreviewAttemptAsync(stepId, response?.Data ?? [], ct);

            if (preview is not null)
            {
                acceptRequiresReview = preview.RequiresReview;
                onAccept = await RouteOfAsync(preview.OnSubmit, ct);
                onClose = await RouteOfAsync(preview.OnTimeout, ct);
            }
        }

        var submittedAt = attempt.Status == FormAttemptStatus.Submitted
            ? response?.SubmittedAt ?? events.LastOrDefault(item => item.Type is FormAttemptEventType.Submitted or FormAttemptEventType.Accepted)?.CreatedAt
            : null;

        return new FormAttemptDetailContract(
            attempt.Id,
            attempt.Status,
            attempt.ResponseId,
            attempt.CreatedAt,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.ExpiredAt,
            submittedAt,
            form.TimeLimitMinutes ?? 0,
            events.Where(item => item.Type == FormAttemptEventType.Extended).Sum(item => item.Minutes ?? 0),
            closedByTeam,
            CanExtend(attempt, closedByTeam),
            attempt.Status == FormAttemptStatus.Provisional,
            CanRemind(attempt, form, now),
            attempt.ReminderSentAt,
            acceptRequiresReview,
            onAccept,
            onClose,
            [.. events
                .OrderBy(item => item.CreatedAt)
                .Select(item => new FormAttemptEventContract(
                    item.Id,
                    item.Type,
                    item.ActorUserId is { } actorId
                        ? actors.FirstOrDefault(user => user.Id == actorId) ?? new UserContract(actorId, null, null, null)
                        : null,
                    item.Minutes,
                    item.Note,
                    item.DeadlineAt,
                    item.CreatedAt))]);
    }

    private async Task<FormAttemptRouteContract?> RouteOfAsync(WorkflowRouteTarget? target, CancellationToken ct)
    {
        if (target is null) return null;
        if (target.FormId is not { } formId) return new FormAttemptRouteContract(true, null, null);

        var form = await _forms.GetByIdAsync(formId, ct);

        return new FormAttemptRouteContract(false, formId, form?.Title);
    }

    private async Task<ResponseWorkflowContract?> StepWorkflowAsync(Guid stepId, CancellationToken ct)
    {
        var context = await _instances.GetContextByStepAsync(stepId, ct);
        if (context is null) return null;

        return new ResponseWorkflowContract(
            context.InstanceId,
            context.Stage,
            [.. context.Steps.Select(step => new ResponseWorkflowStepContract(step.Stage, step.FormTitle, step.ResponseId, step.Status))],
            null,
            null);
    }

    private static FormAttemptStalledQuestionContract? FindStalledQuestion(Form form, IReadOnlyList<List<FormResponseSchemaItem>> drafts)
    {
        var questions = form.Schema.Where(item => item.Type != "separator").ToList();
        if (questions.Count == 0 || drafts.Count == 0) return null;

        var counts = new int[questions.Count];

        foreach (var draft in drafts)
        {
            var answered = draft.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var index = questions.FindIndex(question => !answered.Contains(question.Id));
            if (index >= 0) counts[index]++;
        }

        var top = Array.IndexOf(counts, counts.Max());
        if (counts[top] == 0) return null;

        var stalled = questions[top];

        return new FormAttemptStalledQuestionContract(
            stalled.Id,
            ResponseDataMapper.QuestionOf(stalled),
            top + 1,
            counts[top],
            ResponseDataMapper.IsRequired(stalled));
    }

    private static void Reopen(FormAttempt attempt, DateTime now, int minutes)
    {
        attempt.Status = FormAttemptStatus.Started;
        attempt.ExpiredAt = null;
        attempt.DraftSnapshot = null;
        attempt.DeadlineAt = now.AddMinutes(minutes);
    }

    private static bool CanExtend(FormAttempt attempt, bool closedByTeam) => attempt.Status switch
    {
        FormAttemptStatus.Started or FormAttemptStatus.Provisional => true,
        FormAttemptStatus.NoSubmission => attempt.WorkflowStepId is null && !closedByTeam,
        _ => false
    };

    private bool CanRemind(FormAttempt attempt, Form form, DateTime now) =>
        attempt.Status == FormAttemptStatus.Opened && !form.HasClosedAt(now) && _mail.CanNotifyAttempts;

    private static bool CanManage(Form form, Guid userId) =>
        form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static string DurationText(int minutes)
    {
        if (minutes % 1440 == 0) return $"{minutes / 1440} gün";
        if (minutes % 60 == 0) return $"{minutes / 60} saat";
        return minutes > 60 ? $"{minutes / 60} saat {minutes % 60} dakika" : $"{minutes} dakika";
    }

    private static FormAttemptDisplayContract StartClosedDisplay(Form form) =>
        new(FormAttemptState.StartClosed, form.TimeLimitMinutes ?? 0, form.ClosesAt, null, null, 0, null, false, false);

    private static ServiceResult<FormAttemptStartResult> StartedResult(FormAttemptDisplayContract display, DateTime now, Guid? instanceId) =>
        new(ServiceStatus.Success, new FormAttemptStartResult(display, now, instanceId));

    private static ServiceResult<FormAttemptStartResult> StartClosedResult() =>
        new(ServiceStatus.NotAvailable, Message: "Son başlama saati geçti; görev artık başlatılamaz.");

    private static ServiceResult<ResponseSubmitResult> Closed(string reason, string message) =>
        new(ServiceStatus.NotAvailable, new ResponseSubmitResult(null, null, 0, Reason: reason), message);

    private static ServiceResult<FormAttemptDetailContract> Rejected(string message) =>
        new(ServiceStatus.NotAcceptable, Message: message);

    private static ServiceResult<FormAttemptDetailContract> NotFound() =>
        new(ServiceStatus.NotFound, Message: "Kayıt bulunamadı.");

    private static ServiceResult<FormAttemptDetailContract> NotAuthorized() =>
        new(ServiceStatus.NotAuthorized, Message: "Bu işlem için yetkiniz yok.");

    private static ServiceResult<FormAttemptDetailContract> Conflicted() =>
        new(ServiceStatus.Conflict, Message: "Kayıt bu sırada değişti; sayfayı yenileyip tekrar deneyin.");
}
