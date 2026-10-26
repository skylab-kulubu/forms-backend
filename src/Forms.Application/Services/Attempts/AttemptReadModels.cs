using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Attempts;

/// <summary>Ekibin gördüğü deneme: ayrıntı, kayıt sayfası ve formun deneme analitiği.</summary>
public interface IAttemptReadModels
{
    Task<ServiceResult<FormAttemptViewContract>> GetViewAsync(Guid attemptId, Guid userId, CancellationToken ct = default);
    Task<FormAttemptDetailContract?> GetDetailForResponseAsync(FormResponse response, CancellationToken ct = default);
    Task<Guid?> FindStepIdForResponseAsync(Guid responseId, CancellationToken ct = default);
    Task<ServiceResult<FormAttemptAnalyticsContract>> GetAnalyticsAsync(Guid formId, Guid userId, CancellationToken ct = default);

    /// <summary>Ekibin kararından sonra dönen ayrıntı da buradan kurulur.</summary>
    Task<FormAttemptDetailContract> DetailOfAsync(FormAttempt attempt, Form form, FormResponse? response, CancellationToken ct = default);
}

public sealed class AttemptReadModels(
    IFormAttemptRepository attempts,
    IFormRepository forms,
    IFormWorkflowInstanceRepository instances,
    IFormWorkflowRuntime workflowRuntime,
    IFormMailNotifier mail,
    IUserDirectory users,
    ICurrentUser currentUser,
    TimeProvider time) : IAttemptReadModels
{
    public async Task<ServiceResult<FormAttemptViewContract>> GetViewAsync(Guid attemptId, Guid userId, CancellationToken ct = default)
    {
        var attempt = await attempts.GetWithFormAsync(attemptId, ct);
        if (attempt is null)
            return new ServiceResult<FormAttemptViewContract>(ServiceStatus.NotFound, Message: "Kayıt bulunamadı.");

        if (!attempt.Form.Allows(userId, currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<FormAttemptViewContract>(ServiceStatus.NotAuthorized, Message: "Bu kaydı görüntüleme yetkiniz yok.");

        var user = await users.GetUserAsync(attempt.UserId, ct) ?? new UserContract(attempt.UserId, null, "??", null);
        var detail = await DetailOfAsync(attempt, attempt.Form, null, ct);
        var workflow = attempt.WorkflowStepId is { } stepId ? await StepWorkflowAsync(stepId, ct) : null;

        return new ServiceResult<FormAttemptViewContract>(
            ServiceStatus.Success,
            new FormAttemptViewContract(attempt.Id, attempt.FormId, user, detail, attempt.Form.Task, workflow));
    }

    public async Task<FormAttemptDetailContract?> GetDetailForResponseAsync(FormResponse response, CancellationToken ct = default)
    {
        var attempt = await attempts.GetByResponseAsync(response.Id, ct);

        return attempt is null ? null : await DetailOfAsync(attempt, response.Form, response, ct);
    }

    public async Task<Guid?> FindStepIdForResponseAsync(Guid responseId, CancellationToken ct = default) =>
        (await attempts.GetByResponseAsync(responseId, ct))?.WorkflowStepId;

    public async Task<ServiceResult<FormAttemptAnalyticsContract>> GetAnalyticsAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        var form = await forms.GetByIdAsync(formId, ct);
        if (form is null)
            return new ServiceResult<FormAttemptAnalyticsContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!FormAccess.Allows(await forms.GetRoleAsync(formId, userId, ct), currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<FormAttemptAnalyticsContract>(ServiceStatus.NotAuthorized, Message: "Bu formun analitiğini görüntüleme yetkiniz yok.");

        if (!form.HasTimeLimit)
            return new ServiceResult<FormAttemptAnalyticsContract>(ServiceStatus.NotAcceptable, Message: "Bu formda kişisel süre yok.");

        var stats = await attempts.GetStatsAsync(formId, ct);

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

        var actors = byActor.Count == 0 ? [] : await users.GetUsersAsync(byActor.Select(item => item.ActorId), ct);

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

    public async Task<FormAttemptDetailContract> DetailOfAsync(FormAttempt attempt, Form form, FormResponse? response, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var events = await attempts.GetEventsAsync(attempt.Id, ct);

        var actorIds = events
            .Where(item => item.ActorUserId.HasValue)
            .Select(item => item.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var actors = actorIds.Count == 0 ? [] : await users.GetUsersAsync(actorIds, ct);
        var closedByTeam = events.Any(item => item.Type == FormAttemptEventType.Closed);

        var acceptRequiresReview = form.RequiresManualReview;
        FormAttemptRouteContract? onAccept = null;
        FormAttemptRouteContract? onClose = null;

        if (attempt.Status == FormAttemptStatus.Provisional && attempt.WorkflowStepId is { } stepId)
        {
            var preview = await workflowRuntime.PreviewAttemptAsync(stepId, response?.Data ?? [], ct);

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

        // Yan yana dört bool var; isimli argümanlar yerlerinin sessizce kaymasını engeller.
        return new FormAttemptDetailContract(
            Id: attempt.Id,
            Status: attempt.Status,
            ResponseId: attempt.ResponseId,
            OpenedAt: attempt.CreatedAt,
            StartedAt: attempt.StartedAt,
            DeadlineAt: attempt.DeadlineAt,
            ExpiredAt: attempt.ExpiredAt,
            SubmittedAt: submittedAt,
            TimeLimitMinutes: form.TimeLimitMinutes ?? 0,
            ExtendedMinutes: events.Where(item => item.Type == FormAttemptEventType.Extended).Sum(item => item.Minutes ?? 0),
            ClosedByTeam: closedByTeam,
            CanExtend: attempt.CanBeExtended(closedByTeam),
            CanDecide: attempt.Status == FormAttemptStatus.Provisional,
            CanRemind: attempt.Status == FormAttemptStatus.Opened && !form.HasClosedAt(now) && mail.CanNotifyAttempts,
            ReminderSentAt: attempt.ReminderSentAt,
            AcceptRequiresReview: acceptRequiresReview,
            OnAccept: onAccept,
            OnClose: onClose,
            Events: [.. events
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

        return new FormAttemptRouteContract(false, formId, await forms.GetTitleAsync(formId, ct));
    }

    private async Task<ResponseWorkflowContract?> StepWorkflowAsync(Guid stepId, CancellationToken ct)
    {
        var context = await instances.GetContextByStepAsync(stepId, ct);
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
}
