using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Responses;

/// <summary>Ekibin cevap listesi ve tek bir cevabın ayrıntısı; ayrıntı paylaşım bağlantısıyla da açılır.</summary>
public interface IResponseQueries
{
    Task<ServiceResult<FormResponsesListResult>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, string? token, CancellationToken cancellationToken = default);
}

public sealed class ResponseQueries : IResponseQueries
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IUserDirectory _userService;
    private readonly IFormMailNotifier _mailNotifier;
    private readonly ICurrentUser _currentUser;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly IAttemptReadModels _attemptViews;
    private readonly IResponseShareLinks _shareLinks;
    private readonly TimeProvider _time;

    public ResponseQueries(
        IFormRepository forms,
        IFormResponseRepository responses,
        IUserDirectory userService,
        IFormMailNotifier mailNotifier,
        ICurrentUser currentUser,
        IFormWorkflowRuntime workflowRuntime,
        IFormWorkflowInstanceRepository instances,
        IAttemptReadModels attemptViews,
        IResponseShareLinks shareLinks,
        TimeProvider time)
    {
        _forms = forms;
        _responses = responses;
        _userService = userService;
        _mailNotifier = mailNotifier;
        _currentUser = currentUser;
        _workflowRuntime = workflowRuntime;
        _instances = instances;
        _attemptViews = attemptViews;
        _shareLinks = shareLinks;
        _time = time;
    }

    public async Task<ServiceResult<FormResponsesListResult>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default)
    {
        if (!FormAccess.Allows(await _forms.GetRoleAsync(formId, userId, cancellationToken), _currentUser.IsPlatformAdmin, FormAction.ViewResponses))
            return new ServiceResult<FormResponsesListResult>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");

        var form = await _forms.GetByIdAsync(formId, cancellationToken);
        var hasTimeLimit = form?.HasTimeLimit == true;
        var now = _time.GetUtcNow().UtcDateTime;

        var paged = await _responses.GetPagedAsync(formId, request, hasTimeLimit, now, cancellationToken);

        var userIds = paged.Items.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value)
            .Concat(paged.Items.Where(r => r.ReviewedBy.HasValue).Select(r => r.ReviewedBy!.Value))
            .Distinct()
            .ToList();
        var users = await _userService.GetUsersAsync(userIds, cancellationToken);
        var canRemind = _mailNotifier.CanNotifyAttempts && form?.HasClosedAt(now) == false;

        var mappedItems = paged.Items.Select(r =>
        {
            var userDetail = r.UserId.HasValue ? users.FirstOrDefault(u => u.Id == r.UserId) : null;
            var reviewerDetail = r.ReviewedBy.HasValue ? users.FirstOrDefault(u => u.Id == r.ReviewedBy) : null;

            if (userDetail == null && r.UserId.HasValue)
                userDetail = new UserContract(r.UserId.Value, null, "??", null);

            if (reviewerDetail == null && r.ReviewedBy.HasValue)
                reviewerDetail = new UserContract(r.ReviewedBy.Value, null, "??", null);

            return new ResponseSummaryContract(
                r.Id, userDetail, r.Status, r.IsArchived, reviewerDetail, r.ArchivedBy, r.SubmittedAt, r.ReviewedAt, r.ArchivedAt,
                r.TimeSpent,
                r.Attempt is { } attempt ? ToAttemptSummary(attempt, canRemind) : null,
                r.Guest,
                r.Step is { } step ? new ResponseSummaryWorkflowContract(step.Stage, step.Round) : null);
        }).ToList();

        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var resultData = new PagedResult<ResponseSummaryContract>(mappedItems, paged.TotalCount, page, pageSize);

        var counts = paged.Counts;
        var finalResult = new FormResponsesListResult(
            resultData,
            paged.AverageTimeSpent,
            new ResponseStatusCountsContract(counts.Submitted, counts.Pending, counts.Approved, counts.Declined, counts.Provisional, counts.Running, counts.Opened, counts.NoSubmission, counts.Flagged, counts.Returned),
            paged.AverageTaskSeconds,
            hasTimeLimit);

        return new ServiceResult<FormResponsesListResult>(ServiceStatus.Success, Data: finalResult);
    }

    private static ResponseAttemptSummaryContract ToAttemptSummary(ResponseAttemptProjection attempt, bool canRemind) =>
        new(
            attempt.Id,
            attempt.Status,
            attempt.OpenedAt,
            attempt.StartedAt,
            attempt.DeadlineAt,
            attempt.ExpiredAt,
            attempt.ExtendedMinutes,
            attempt.ClosedByTeam,
            FormAttempt.CanBeExtended(attempt.Status, attempt.WorkflowStepId, attempt.ClosedByTeam),
            canRemind && attempt.Status == FormAttemptStatus.Opened,
            attempt is { StartedAt: { } startedAt, ResponseSubmittedAt: { } submittedAt, Status: FormAttemptStatus.Submitted }
                ? (int)Math.Max(0, (submittedAt - startedAt).TotalSeconds)
                : null);

    public async Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<ResponseContract>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        var canView = response.Form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.ViewResponses);

        ShareGrant? shareEntry = null;
        if (!canView)
        {
            if (string.IsNullOrEmpty(token))
                return new ServiceResult<ResponseContract>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı görüntüleme yetkiniz yok.");

            shareEntry = await _shareLinks.FindGrantAsync(responseId, token, cancellationToken);
            if (shareEntry == null)
                return new ServiceResult<ResponseContract>(ServiceStatus.NotAuthorized, Message: "Paylaşım bağlantısı geçersiz veya süresi dolmuş.");
        }

        var workflow = await BuildWorkflowContractAsync(response, userId, cancellationToken);
        var voidedById = workflow?.Run?.VoidedBy?.Id;

        var userIds = new List<Guid>();
        if (response.UserId.HasValue) userIds.Add(response.UserId.Value);
        if (response.ReviewedBy.HasValue) userIds.Add(response.ReviewedBy.Value);
        if (response.ArchivedBy.HasValue) userIds.Add(response.ArchivedBy.Value);
        if (shareEntry != null) userIds.Add(shareEntry.SharedByUserId);
        if (voidedById.HasValue) userIds.Add(voidedById.Value);

        var users = await _userService.GetUsersAsync(userIds, cancellationToken);

        var responderUser = response.UserId.HasValue ? users.FirstOrDefault(u => u.Id == response.UserId) : null;
        var reviewerUser = response.ReviewedBy.HasValue ? users.FirstOrDefault(u => u.Id == response.ReviewedBy) : null;
        var archiverUser = response.ArchivedBy.HasValue ? users.FirstOrDefault(u => u.Id == response.ArchivedBy) : null;
        var sharedByUser = shareEntry != null ? users.FirstOrDefault(u => u.Id == shareEntry.SharedByUserId) : null;

        if (workflow?.Run is { } run && users.FirstOrDefault(u => u.Id == voidedById) is { } voidedBy)
            workflow = workflow with { Run = run with { VoidedBy = voidedBy } };

        var attempt = canView ? await _attemptViews.GetDetailForResponseAsync(response, cancellationToken) : null;

        return new ServiceResult<ResponseContract>(
            ServiceStatus.Success,
            Data: MapToDetailContract(response, workflow, responderUser, reviewerUser, archiverUser, sharedByUser) with
            {
                Attempt = attempt,
                Task = response.Form.Task
            }
        );
    }

    private static ResponseContract MapToDetailContract(FormResponse response, ResponseWorkflowContract? workflow, UserContract? responderUser, UserContract? reviewerUser, UserContract? archiverUser, UserContract? sharedByUser = null)
    {
        return new ResponseContract(
            response.Id,
            response.FormId,
            responderUser,
            reviewerUser,
            archiverUser,
            response.Data,
            response.TimeSpent,
            response.Status,
            response.IsArchived,
            workflow,
            response.ReviewNote,
            response.SubmittedAt,
            response.ReviewedAt,
            response.ArchivedAt,
            sharedByUser,
            response.Attribution,
            Guest: response.Guest,
            ReturnedQuestionIds: response.ReturnedQuestionIds
        );
    }

    private async Task<ResponseWorkflowContract?> BuildWorkflowContractAsync(FormResponse response, Guid viewerId, CancellationToken cancellationToken)
    {
        var context = await _instances.GetContextByResponseAsync(response.Id, cancellationToken);

        if (context is null && response.Status == FormResponseStatus.Provisional
            && await _attemptViews.FindStepIdForResponseAsync(response.Id, cancellationToken) is { } stepId
            && await _instances.GetContextByStepAsync(stepId, cancellationToken) is { } stepContext)
        {
            context = stepContext with
            {
                Steps = [.. stepContext.Steps.Select(step => step.Stage == stepContext.Stage && step.Round == stepContext.Round
                    ? step with { ResponseId = response.Id, Status = FormResponseStatus.Provisional }
                    : step)]
            };
        }

        if (context is null) return null;

        var steps = context.Steps
            .Select(step => new ResponseWorkflowStepContract(step.Stage, step.FormTitle, step.ResponseId, step.Status, step.Round, step.SubmittedAt, step.ReviewedAt))
            .ToList();

        var preview = await _workflowRuntime.PreviewReviewAsync(response, cancellationToken);
        var canReturn = preview is not null && !response.Form.HasTimeLimit && !response.Form.HasClosedAt(_time.GetUtcNow().UtcDateTime);

        return new ResponseWorkflowContract(
            context.InstanceId,
            context.Stage,
            steps,
            await ToRouteContractAsync(preview?.OnApprove, cancellationToken),
            await ToRouteContractAsync(preview?.OnDecline, cancellationToken),
            context.Round,
            canReturn,
            await BuildRunContractAsync(context.InstanceId, response.Form, viewerId, cancellationToken));
    }

    /// <summary>VoidedBy yalnız kimlikle gelir; çağıran kişi bilgisini diğer kullanıcılarla birlikte doldurur.</summary>
    private async Task<ResponseRunContract?> BuildRunContractAsync(Guid instanceId, Form form, Guid viewerId, CancellationToken cancellationToken)
    {
        var run = await _instances.GetRunFactsAsync(instanceId, cancellationToken);
        if (run is null) return null;

        var canManage = run.WorkflowOwnerId == viewerId || form.Allows(viewerId, _currentUser.IsPlatformAdmin, FormAction.VoidApplication);
        var block = WorkflowRunVoidBlock.Of(run);

        return new ResponseRunContract(
            run.Status,
            run.Outcome,
            run.AllowMultipleRuns,
            FormWorkflow.EffectiveIntake(run.Intake, run.IntakeClosesAt, _time.GetUtcNow().UtcDateTime),
            canManage && block is null,
            block,
            run.CompletedAt,
            run.VoidedAt,
            run.VoidedBy is { } voidedBy ? new UserContract(voidedBy, null, null, null) : null,
            run.VoidNote);
    }

    private async Task<ResponseWorkflowRouteContract?> ToRouteContractAsync(
        WorkflowRouteTarget? target,
        CancellationToken cancellationToken)
    {
        if (target is null) return null;

        if (target.FormId is not { } formId)
            return new ResponseWorkflowRouteContract(EndsFlow: true, FormId: null, FormTitle: null);

        return new ResponseWorkflowRouteContract(EndsFlow: false, formId, await _forms.GetTitleAsync(formId, cancellationToken));
    }
}
