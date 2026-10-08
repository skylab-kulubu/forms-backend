using System.Net;
using System.Security.Cryptography;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Attribution;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Exports;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services;

public class FormResponseService : IFormResponseService
{
    private static readonly TimeSpan ShareTokenLifetime = TimeSpan.FromHours(1);
    private const string TokenKeyPrefix = "response:share:token:";
    private const string ResponseKeyPrefix = "response:share:response:";

    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IFormsUnitOfWork _uow;
    private readonly IExternalUserService _userService;
    private readonly IExcelService _excelService;
    private readonly IFormDraftService _draftService;
    private readonly ICacheService _cache;
    private readonly IFormMailNotifier _mailNotifier;
    private readonly ICurrentUserService _currentUserService;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly IFormAttemptService _attempts;
    private readonly IFormAttemptRepository _attemptRecords;
    private readonly IGuestUploadService _guestUploads;
    private readonly ICoreMedia _media;

    public FormResponseService(
        IFormRepository forms,
        IFormResponseRepository responses,
        IFormsUnitOfWork uow,
        IExternalUserService userService,
        IExcelService excelService,
        IFormDraftService draftService,
        ICacheService cache,
        IFormMailNotifier mailNotifier,
        ICurrentUserService currentUserService,
        IFormWorkflowRuntime workflowRuntime,
        IFormWorkflowInstanceRepository instances,
        IFormAttemptService attempts,
        IFormAttemptRepository attemptRecords,
        IGuestUploadService guestUploads,
        ICoreMedia media)
    {
        _attempts = attempts;
        _attemptRecords = attemptRecords;
        _guestUploads = guestUploads;
        _media = media;
        _forms = forms;
        _responses = responses;
        _uow = uow;
        _userService = userService;
        _excelService = excelService;
        _draftService = draftService;
        _cache = cache;
        _mailNotifier = mailNotifier;
        _currentUserService = currentUserService;
        _workflowRuntime = workflowRuntime;
        _instances = instances;
    }

    private record ShareCacheEntry(Guid ResponseId, List<Guid> InstanceResponseIds, Guid SharedByUserId);

    public async Task<ServiceResult<ResponseSubmitResult>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, IPAddress? clientAddress, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(contract.FormId, cancellationToken);
        if (form == null) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotFound, Message: "Form bulunamadı.");
        if (form.Status != FormStatus.Open) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAvailable, Message: "Bu form şu anda yanıt kabul etmiyor.");

        if (!form.HasTimeLimit && form.HasClosedAt(DateTime.UtcNow))
        {
            return new ServiceResult<ResponseSubmitResult>(
                ServiceStatus.NotAvailable,
                new ResponseSubmitResult(null, null, 0, Reason: FormClosedReason.Closed),
                "Bu form kapanış saatinde kendiliğinden kapandı.");
        }

        if (!form.AllowAnonymousResponses && userId == null) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.Unauthorized, Message: "Bu formu doldurmak için giriş yapmalısınız.");

        ResponseGuest? guest = null;

        if (userId == null && ResponseGuest.IsAskedBy(form.Schema))
        {
            guest = ResponseGuest.From(form.Schema, contract.Responses);
            if (guest is null) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Ad, soyad ve e-posta zorunludur.");
        }

        var guestGate = GuestSubmitGate.Pass;
        if (userId == null)
        {
            guestGate = await _guestUploads.CheckSubmitAsync(form, contract, clientAddress, cancellationToken);
            if (guestGate.Rejection is not null) return guestGate.Rejection;
        }

        Guid? attemptId = null;

        if (userId.HasValue && form.HasTimeLimit)
        {
            var gate = await _attempts.CheckSubmitAsync(form, userId.Value, cancellationToken);
            if (gate.Rejection is not null) return gate.Rejection;

            attemptId = gate.Attempt?.Id;
        }

        if (userId.HasValue)
        {
            var workflowResult = await SubmitThroughWorkflowAsync(form, contract, userId.Value, cancellationToken);

            if (workflowResult is not null)
            {
                if (attemptId.HasValue && !workflowResult.Status.IsFailure() && workflowResult.Data?.ResponseId is { } workflowResponseId)
                    await _attempts.MarkSubmittedAsync(attemptId.Value, workflowResponseId, cancellationToken);

                return workflowResult;
            }
        }

        if (userId.HasValue && !form.AllowMultipleResponses && !attemptId.HasValue)
        {
            var hasExistingResponse = await _responses.HasNonArchivedResponseAsync(form.Id, userId.Value, cancellationToken);
            if (hasExistingResponse) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz.");
        }

        var response = MapToEntity(form, contract.Responses, contract.TimeSpent, userId, contract.Attribution, guest);
        if (!guestGate.Verified) response.Status = FormResponseStatus.Flagged;

        IReadOnlyList<GuestAttachment> attachments = [];
        if (guestGate.Files.Count > 0)
        {
            var attach = await _guestUploads.AttachAsync(guestGate.Files, response.Id, cancellationToken);
            if (attach.Rejection is not null) return attach.Rejection;

            attachments = attach.Attachments;
        }

        try
        {
            _responses.Add(response);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _guestUploads.DetachAsync(attachments);
            throw;
        }

        if (guestGate.SessionId is { } guestSessionId)
            await _guestUploads.ConsumeAsync(guestSessionId, guestGate.Files);

        if (attemptId.HasValue)
            await _attempts.MarkSubmittedAsync(attemptId.Value, response.Id, cancellationToken);

        await AfterResponseSavedAsync(form, response, cancellationToken, sendCopy: guestGate.Verified);

        var underReview = form.RequiresManualReview || response.Status == FormResponseStatus.Flagged;
        var status = underReview ? ServiceStatus.PendingApproval : ServiceStatus.Success;
        var message = underReview ? "Yanıtınız incelemeye alındı." : "Yanıt kaydedildi.";

        var result = new ResponseSubmitResult(response.Id, LinkedFormId: null, Step: 0);
        return new ServiceResult<ResponseSubmitResult>(status, Data: result, Message: message);
    }

    private async Task<ServiceResult<ResponseSubmitResult>?> SubmitThroughWorkflowAsync(
        Form form,
        ResponseSubmitRequest contract,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var response = MapToEntity(form, contract.Responses, contract.TimeSpent, userId, contract.Attribution);
        var workflow = await _workflowRuntime.SubmitAsync(form, response, userId, cancellationToken);

        if (workflow.Data is { State: WorkflowActionState.NotInWorkflow }) return null;

        if (workflow.Data is not { } outcome)
            return new ServiceResult<ResponseSubmitResult>(workflow.Status, Message: workflow.Message);

        var rejected = workflow.Status.IsFailure();

        if (!rejected) await AfterResponseSavedAsync(form, response, cancellationToken);

        var result = new ResponseSubmitResult(
            rejected ? null : response.Id,
            LinkedFormId: outcome.IsLegacyTwoStepFlow ? outcome.FormId : null,
            LegacyStep.From(outcome),
            outcome.InstanceId,
            outcome.State,
            outcome.Stage,
            outcome.FormId,
            outcome.StartFormId,
            outcome.Reason,
            outcome.Journey);

        return new ServiceResult<ResponseSubmitResult>(workflow.Status, result, workflow.Message);
    }

    private async Task AfterResponseSavedAsync(Form form, FormResponse response, CancellationToken cancellationToken, bool sendCopy = true)
    {
        await _cache.TryRemoveAsync(FormCacheKeys.Analytics(form.Id), cancellationToken);

        if (response.UserId.HasValue)
            await _draftService.DeleteResponseDraftAsync(form.Id, response.UserId.Value, cancellationToken);

        if (sendCopy)
            await _mailNotifier.NotifyResponseCopyAsync(form, response, cancellationToken);
    }

    public async Task<ServiceResult<FormResponsesListResult>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default)
    {
        var isAuthorized = await _forms.IsUserCollaboratorAsync(formId, userId, cancellationToken);
        if (!isAuthorized && !await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken))
            return new ServiceResult<FormResponsesListResult>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");

        var form = await _forms.GetByIdAsync(formId, cancellationToken);
        var hasTimeLimit = form?.HasTimeLimit == true;
        var now = DateTime.UtcNow;

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
                r.Guest);
        }).ToList();

        var resultData = new PagedResult<ResponseSummaryContract>(
            mappedItems,
            paged.TotalCount,
            request.Page,
            request.PageSize
        );

        var counts = paged.Counts;
        var finalResult = new FormResponsesListResult(
            resultData,
            paged.AverageTimeSpent,
            new ResponseStatusCountsContract(counts.Submitted, counts.Pending, counts.Approved, counts.Declined, counts.Provisional, counts.Running, counts.Opened, counts.NoSubmission, counts.Flagged),
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
            attempt.Status switch
            {
                FormAttemptStatus.Started or FormAttemptStatus.Provisional => true,
                FormAttemptStatus.NoSubmission => attempt.WorkflowStepId is null && !attempt.ClosedByTeam,
                _ => false
            },
            canRemind && attempt.Status == FormAttemptStatus.Opened,
            attempt is { StartedAt: { } startedAt, ResponseSubmittedAt: { } submittedAt, Status: FormAttemptStatus.Submitted }
                ? (int)Math.Max(0, (submittedAt - startedAt).TotalSeconds)
                : null);

    public async Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<ResponseContract>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        var isCollaborator = response.Form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        var canView = isCollaborator || await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken);

        ShareCacheEntry? shareEntry = null;
        if (!canView)
        {
            if (string.IsNullOrEmpty(token))
                return new ServiceResult<ResponseContract>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı görüntüleme yetkiniz yok.");

            shareEntry = await _cache.GetAsync<ShareCacheEntry>(TokenKeyPrefix + token, ct: cancellationToken);
            if (shareEntry == null || (shareEntry.ResponseId != responseId && !shareEntry.InstanceResponseIds.Contains(responseId)))
                return new ServiceResult<ResponseContract>(ServiceStatus.NotAuthorized, Message: "Paylaşım bağlantısı geçersiz veya süresi dolmuş.");
        }

        var workflow = await BuildWorkflowContractAsync(response, cancellationToken);

        var userIds = new List<Guid>();
        if (response.UserId.HasValue) userIds.Add(response.UserId.Value);
        if (response.ReviewedBy.HasValue) userIds.Add(response.ReviewedBy.Value);
        if (response.ArchivedBy.HasValue) userIds.Add(response.ArchivedBy.Value);
        if (shareEntry != null) userIds.Add(shareEntry.SharedByUserId);

        var users = await _userService.GetUsersAsync(userIds, cancellationToken);

        var responderUser = response.UserId.HasValue ? users.FirstOrDefault(u => u.Id == response.UserId) : null;
        var reviewerUser = response.ReviewedBy.HasValue ? users.FirstOrDefault(u => u.Id == response.ReviewedBy) : null;
        var archiverUser = response.ArchivedBy.HasValue ? users.FirstOrDefault(u => u.Id == response.ArchivedBy) : null;
        var sharedByUser = shareEntry != null ? users.FirstOrDefault(u => u.Id == shareEntry.SharedByUserId) : null;

        var attempt = canView ? await _attempts.GetDetailForResponseAsync(response, cancellationToken) : null;

        return new ServiceResult<ResponseContract>(
            ServiceStatus.Success,
            Data: MapToDetailContract(response, workflow, responderUser, reviewerUser, archiverUser, sharedByUser) with
            {
                Attempt = attempt,
                Task = response.Form.Task
            }
        );
    }

    public async Task<ServiceResult<ResponseFileContract>> GetResponseFileAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        if (await CheckFileAccessAsync(responseId, mediaId, userId, token, cancellationToken) is { } denied)
            return new ServiceResult<ResponseFileContract>(denied.Status, Message: denied.Message);

        var read = await _media.GetAsync(mediaId, cancellationToken);

        if (read.Outcome == CoreMediaOutcome.NotFound)
        {
            return new ServiceResult<ResponseFileContract>(
                ServiceStatus.Success,
                new ResponseFileContract(mediaId, null, null, null, ResponseFileStatus.Deleted, null, false, null));
        }

        if (read.Outcome != CoreMediaOutcome.Ok || read.Media is not { } media)
            return new ServiceResult<ResponseFileContract>(ServiceStatus.ServiceUnavailable, Message: "Dosya bilgisi şu an alınamıyor. Biraz sonra tekrar deneyin.");

        var isPrivate = string.Equals(media.Visibility, CoreMediaVisibility.Private, StringComparison.Ordinal);
        var status = media.Status switch
        {
            CoreMediaStatus.Scanning => ResponseFileStatus.Scanning,
            CoreMediaStatus.Rejected => ResponseFileStatus.Rejected,
            _ => ResponseFileStatus.Ready
        };

        return new ServiceResult<ResponseFileContract>(
            ServiceStatus.Success,
            new ResponseFileContract(
                mediaId,
                media.Name,
                media.Type,
                media.Size,
                status,
                media.ScanResult,
                isPrivate,
                !isPrivate && !string.IsNullOrEmpty(media.Url) ? media.Url : null));
    }

    public async Task<ServiceResult<ResponseFileLinkContract>> CreateResponseFileLinkAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        if (await CheckFileAccessAsync(responseId, mediaId, userId, token, cancellationToken) is { } denied)
            return new ServiceResult<ResponseFileLinkContract>(denied.Status, Message: denied.Message);

        var link = await _media.CreateLinkAsync(mediaId, userId, cancellationToken);

        return link.Outcome switch
        {
            CoreMediaOutcome.Ok when !string.IsNullOrEmpty(link.Url) => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.Success,
                new ResponseFileLinkContract(link.Url, link.ExpiresAt)),
            CoreMediaOutcome.Scanning => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.Conflict,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Scanning, RetryAfterSeconds: link.RetryAfterSeconds),
                "Dosya hâlâ taranıyor. Biraz sonra tekrar deneyin."),
            CoreMediaOutcome.Rejected => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.NotAvailable,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Rejected, ScanResult: link.ScanResult),
                "Dosya güvenlik taramasından geçmedi."),
            CoreMediaOutcome.NotFound => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.NotFound,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Deleted),
                "Dosya artık mevcut değil."),
            CoreMediaOutcome.SubjectInactive => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.NotAuthorized,
                new ResponseFileLinkContract(Reason: ResponseFileReason.SubjectInactive),
                "Hesabınız için dosya bağlantısı oluşturulamadı."),
            _ => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.ServiceUnavailable,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Unavailable),
                "Dosya şu an açılamıyor. Biraz sonra tekrar deneyin.")
        };
    }

    private async Task<(ServiceStatus Status, string Message)?> CheckFileAccessAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);
        if (response == null) return (ServiceStatus.NotFound, "Yanıt bulunamadı.");

        var isCollaborator = response.Form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        var canView = isCollaborator || await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken);

        if (!canView)
        {
            if (string.IsNullOrEmpty(token)) return (ServiceStatus.NotAuthorized, "Bu yanıtı görüntüleme yetkiniz yok.");

            var shareEntry = await _cache.GetAsync<ShareCacheEntry>(TokenKeyPrefix + token, ct: cancellationToken);
            if (shareEntry == null || (shareEntry.ResponseId != responseId && !shareEntry.InstanceResponseIds.Contains(responseId)))
                return (ServiceStatus.NotAuthorized, "Paylaşım bağlantısı geçersiz veya süresi dolmuş.");
        }

        var holdsFile = response.Data.Any(item =>
            item.Type == GuestUploadRules.FileQuestionType
            && Guid.TryParse(item.Answer, out var answer)
            && answer == mediaId);

        return holdsFile ? null : (ServiceStatus.NotFound, "Dosya bulunamadı.");
    }

    public async Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(contract.ResponseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == reviewerId && c.Role != CollaboratorRole.None);

        if (!isAuthorized)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı onaylama veya reddetme yetkiniz yok.");

        if (response.IsArchived)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş yanıtlar üzerinde değişiklik yapılamaz.");

        if (contract.Note?.Length > FormResponse.ReviewNoteMaxLength)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: $"Açıklama en fazla {FormResponse.ReviewNoteMaxLength} karakter olabilir.");

        if (response.Status == FormResponseStatus.Provisional)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Geçici cevap için önce karar verin: teslim olarak kabul edin, süre verin ya da kapatın.");

        if (contract.NewStatus == FormResponseStatus.Provisional)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Bir cevap geçici duruma alınamaz.");

        if (contract.NewStatus == FormResponseStatus.Flagged)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Bir cevap doğrulanmamış duruma alınamaz.");

        var workflow = await _workflowRuntime.ReviewAsync(response, contract.NewStatus, reviewerId, contract.Note, cancellationToken);

        if (workflow.Data is not { State: WorkflowActionState.NotInWorkflow })
        {
            if (workflow.Status.IsFailure() || workflow.Data is null)
                return new ServiceResult<bool>(workflow.Status, Message: workflow.Message);

            await _mailNotifier.NotifyStatusChangedAsync(response.Form, response, workflow.Data.FormId, cancellationToken);

            return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Yanıt durumu güncellendi ve akış ilerletildi.");
        }

        var declinesFlagged = response.Status == FormResponseStatus.Flagged && contract.NewStatus == FormResponseStatus.Declined;

        response.ApplyReview(contract.NewStatus, reviewerId, contract.Note, DateTime.UtcNow);

        await _uow.SaveChangesAsync(cancellationToken);

        if (!declinesFlagged)
            await _mailNotifier.NotifyStatusChangedAsync(response.Form, response, ct: cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Yanıt durumu başarıyla güncellendi.");
    }

    public async Task<ServiceResult<bool>> ArchiveResponseAsync(Guid responseId, Guid archiverId, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == archiverId && c.Role != CollaboratorRole.None);

        if (!isAuthorized)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı arşivleme yetkiniz yok.");

        if (response.IsArchived)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Bu yanıt zaten arşivlenmiş.");

        if (response.Status == FormResponseStatus.Provisional)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Geçici cevap arşivlenemez; önce karar verin.");

        if (response.Status is FormResponseStatus.Pending or FormResponseStatus.Flagged)
        {
            if (await _workflowRuntime.HasPendingRouteAsync(responseId, cancellationToken))
            {
                return new ServiceResult<bool>(
                    ServiceStatus.NotAcceptable,
                    Message: "Akış içindeki bekleyen bir cevap arşivlenemez; önce onaylayın veya reddedin.");
            }

            response.Status = FormResponseStatus.Declined;
            response.ReviewNote = "Arşivlendiği için sistem tarafından otomatik olarak reddedildi.";
            response.ReviewedBy = archiverId;
            response.ReviewedAt = DateTime.UtcNow;
        }

        response.IsArchived = true;
        response.ArchivedBy = archiverId;
        response.ArchivedAt = DateTime.UtcNow;

        await _uow.SaveChangesAsync(cancellationToken);

        await _cache.TryRemoveAsync(FormCacheKeys.Analytics(response.FormId), cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Yanıt başarıyla arşivlendi.");
    }

    public async Task<ServiceResult<byte[]>> ExportResponsesToExcelAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, cancellationToken);

        if (form == null)
            return new ServiceResult<byte[]>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        if (!isAuthorized && !await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken))
            return new ServiceResult<byte[]>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını dışa aktarma yetkiniz yok.");

        var responses = await _responses.GetNonArchivedByFormAsync(formId, cancellationToken);

        var headers = new List<string>
        {
            "Yanıt ID",
            "Kullanıcı ID",
            "Gönderim Tarihi",
            "Durum",
            "İncelenme Notu",
            "Kaynak",
            "Ortam",
            "Kampanya"
        };

        var timings = form.HasTimeLimit
            ? await _attemptRecords.GetTimingsByResponseAsync(formId, cancellationToken)
            : new Dictionary<Guid, Abstractions.Storage.FormAttemptTiming>();

        if (form.HasTimeLimit)
            headers.AddRange(["Başlama Tarihi", "Teslim Süresi (dk)", "Ek Süre (dk)"]);

        foreach (var schemaItem in form.Schema)
        {
            string questionText = schemaItem.Props.TryGetValue("question", out var qVal) ? qVal?.ToString() ?? "İsimsiz Soru" : "Soru";
            headers.Add(questionText);
        }

        var rows = new List<List<string>>();

        foreach (var response in responses)
        {
            var row = new List<string>
            {
                response.Id.ToString(),
                response.UserId?.ToString() ?? "Anonim",
                response.SubmittedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                response.Status.ToString(),
                response.ReviewNote ?? "",
                response.Attribution?.Source ?? "",
                response.Attribution?.Medium ?? "",
                response.Attribution?.Campaign ?? ""
            };

            if (form.HasTimeLimit)
            {
                var timing = timings.TryGetValue(response.Id, out var found) ? found : null;

                row.Add(timing?.StartedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "");
                row.Add(timing?.StartedAt is { } startedAt ? Math.Max(0, (int)Math.Round((response.SubmittedAt - startedAt).TotalMinutes)).ToString() : "");
                row.Add(timing is { ExtendedMinutes: > 0 } ? timing.ExtendedMinutes.ToString() : "");
            }

            foreach (var schemaItem in form.Schema)
            {
                var answerItem = response.Data.FirstOrDefault(d => d.Id == schemaItem.Id);
                row.Add(FormAnswerText.ToDisplayText(answerItem?.Answer));
            }

            rows.Add(row);
        }

        string sheetName = form.Title.Length > 31 ? form.Title.Substring(0, 31) : form.Title;

        var exportRequest = new ExcelExportRequest(sheetName, headers, rows);
        return _excelService.GenerateExcel(exportRequest);
    }

    private static FormResponse MapToEntity(Form form, List<FormResponseSchemaItem> userResponses, int? timeSpent, Guid? userId, ResponseAttributionRequest? attribution, ResponseGuest? guest = null)
    {
        var responseData = new List<FormResponseSchemaItem>();

        foreach (var schemaItem in form.Schema)
        {
            var userAnswer = userResponses.FirstOrDefault(r => r.Id == schemaItem.Id)?.Answer ?? string.Empty;

            string questionText = "";
            if (schemaItem.Props.TryGetValue("question", out var qVal) && qVal != null) questionText = qVal.ToString() ?? "";

            responseData.Add(new FormResponseSchemaItem
            {
                Id = schemaItem.Id,
                Type = schemaItem.Type,
                Question = questionText,
                Answer = userAnswer
            });
        }

        return new FormResponse
        {
            FormId = form.Id,
            UserId = userId,
            Data = responseData,
            TimeSpent = timeSpent,
            Status = form.RequiresManualReview ? FormResponseStatus.Pending : FormResponseStatus.NonRestrict,
            SubmittedAt = DateTime.UtcNow,
            Attribution = AttributionNormalizer.Normalize(attribution),
            Guest = guest
        };
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
            Guest: response.Guest
        );
    }

    public async Task<ServiceResult<ShareTokenContract>> CreateOrRefreshShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<ShareTokenContract>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        var isCollaborator = response.Form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        if (!isCollaborator)
            return new ServiceResult<ShareTokenContract>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı paylaşma yetkiniz yok.");

        var context = await _instances.GetContextByResponseAsync(responseId, cancellationToken);

        var relatedResponseIds = (context?.Steps ?? [])
            .Where(step => step.ResponseId.HasValue && step.ResponseId.Value != responseId)
            .Select(step => step.ResponseId!.Value)
            .ToList();

        var existingToken = await _cache.GetAsync<string>(ResponseKeyPrefix + responseId, ct: cancellationToken);
        var token = existingToken ?? GenerateToken();

        var entry = new ShareCacheEntry(responseId, relatedResponseIds, userId);
        var expiresAt = DateTime.UtcNow.Add(ShareTokenLifetime);

        await _cache.SetAsync(TokenKeyPrefix + token, entry, ShareTokenLifetime, cancellationToken);
        await _cache.SetAsync(ResponseKeyPrefix + responseId, token, ShareTokenLifetime, cancellationToken);

        foreach (var relatedId in relatedResponseIds)
            await _cache.SetAsync(ResponseKeyPrefix + relatedId, token, ShareTokenLifetime, cancellationToken);

        return new ServiceResult<ShareTokenContract>(ServiceStatus.Success, Data: new ShareTokenContract(token, expiresAt));
    }

    public async Task<ServiceResult<bool>> RevokeShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        var isCollaborator = response.Form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        if (!isCollaborator)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtın paylaşımını iptal etme yetkiniz yok.");

        var token = await _cache.GetAsync<string>(ResponseKeyPrefix + responseId, ct: cancellationToken);
        if (string.IsNullOrEmpty(token))
            return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Aktif paylaşım yok.");

        var entry = await _cache.GetAsync<ShareCacheEntry>(TokenKeyPrefix + token, ct: cancellationToken);

        await _cache.RemoveAsync(TokenKeyPrefix + token, cancellationToken);
        await _cache.RemoveAsync(ResponseKeyPrefix + (entry?.ResponseId ?? responseId), cancellationToken);

        foreach (var relatedId in entry?.InstanceResponseIds ?? [])
            await _cache.RemoveAsync(ResponseKeyPrefix + relatedId, cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Paylaşım iptal edildi.");
    }

    public async Task<ServiceResult<ResponseMetaContract>> GetResponseMetaAsync(Guid responseId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            return new ServiceResult<ResponseMetaContract>(ServiceStatus.NotFound);

        var entry = await _cache.GetAsync<ShareCacheEntry>(TokenKeyPrefix + token, ct: cancellationToken);
        if (entry == null || (entry.ResponseId != responseId && !entry.InstanceResponseIds.Contains(responseId)))
            return new ServiceResult<ResponseMetaContract>(ServiceStatus.NotFound);

        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);
        if (response == null)
            return new ServiceResult<ResponseMetaContract>(ServiceStatus.NotFound);

        var sharedBy = await _userService.GetUserAsync(entry.SharedByUserId, cancellationToken);

        return new ServiceResult<ResponseMetaContract>(
            ServiceStatus.Success,
            Data: new ResponseMetaContract(response.Form.Title, sharedBy)
        );
    }

    private async Task<ResponseWorkflowContract?> BuildWorkflowContractAsync(FormResponse response, CancellationToken cancellationToken)
    {
        var context = await _instances.GetContextByResponseAsync(response.Id, cancellationToken);

        if (context is null && response.Status == FormResponseStatus.Provisional
            && await _attempts.FindStepIdForResponseAsync(response.Id, cancellationToken) is { } stepId
            && await _instances.GetContextByStepAsync(stepId, cancellationToken) is { } stepContext)
        {
            context = stepContext with
            {
                Steps = [.. stepContext.Steps.Select(step => step.Stage == stepContext.Stage
                    ? step with { ResponseId = response.Id, Status = FormResponseStatus.Provisional }
                    : step)]
            };
        }

        if (context is null) return null;

        var steps = context.Steps
            .Select(step => new ResponseWorkflowStepContract(step.Stage, step.FormTitle, step.ResponseId, step.Status))
            .ToList();

        var preview = await _workflowRuntime.PreviewReviewAsync(response, cancellationToken);

        return new ResponseWorkflowContract(
            context.InstanceId,
            context.Stage,
            steps,
            await ToRouteContractAsync(preview?.OnApprove, cancellationToken),
            await ToRouteContractAsync(preview?.OnDecline, cancellationToken));
    }

    private async Task<ResponseWorkflowRouteContract?> ToRouteContractAsync(
        WorkflowRouteTarget? target,
        CancellationToken cancellationToken)
    {
        if (target is null) return null;

        if (target.FormId is not { } formId)
            return new ResponseWorkflowRouteContract(EndsFlow: true, FormId: null, FormTitle: null);

        var form = await _forms.GetByIdAsync(formId, cancellationToken);

        return new ResponseWorkflowRouteContract(EndsFlow: false, formId, form?.Title);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
