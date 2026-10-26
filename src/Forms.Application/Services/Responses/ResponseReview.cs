using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Responses;

public interface IResponseReview
{
    Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> ArchiveResponseAsync(Guid responseId, Guid archiverId, CancellationToken cancellationToken = default);
}

public sealed class ResponseReview : IResponseReview
{
    private readonly IFormResponseRepository _responses;
    private readonly IFormsUnitOfWork _uow;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormMailNotifier _mailNotifier;
    private readonly ICacheService _cache;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public ResponseReview(
        IFormResponseRepository responses,
        IFormsUnitOfWork uow,
        IFormWorkflowRuntime workflowRuntime,
        IFormMailNotifier mailNotifier,
        ICacheService cache,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _responses = responses;
        _uow = uow;
        _workflowRuntime = workflowRuntime;
        _mailNotifier = mailNotifier;
        _cache = cache;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(contract.NewStatus))
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Bilinmeyen bir cevap durumu gönderildi.");

        var response = await _responses.GetForEditByIdWithFormAndCollaboratorsAsync(contract.ResponseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        if (!response.Form.Allows(reviewerId, _currentUser.IsPlatformAdmin, FormAction.HandleResponses))
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

        var returning = contract.NewStatus == FormResponseStatus.Returned;

        if (returning && string.IsNullOrWhiteSpace(contract.Note))
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Düzeltme isterken adaya neyi düzelteceğini yazın.");

        var workflow = await _workflowRuntime.ReviewAsync(response, contract.NewStatus, reviewerId, contract.Note, contract.QuestionIds, cancellationToken);

        if (workflow.Data is not { State: WorkflowActionState.NotInWorkflow })
        {
            if (workflow.Status.IsFailure() || workflow.Data is null)
                return new ServiceResult<bool>(workflow.Status, Message: workflow.Message);

            // Düzeltme istenen cevap analize girmez; önbellekteki sonuç onu saymaya devam etmesin.
            if (returning) await _cache.TryRemoveAsync(FormCacheKeys.Analytics(response.FormId), cancellationToken);

            await _mailNotifier.NotifyStatusChangedAsync(response.Form, response, workflow.Data.FormId, cancellationToken);

            return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: returning
                ? "Düzeltme istendi; aday formu önceki cevaplarıyla yeniden açabilir."
                : "Yanıt durumu güncellendi ve akış ilerletildi.");
        }

        if (returning)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Düzeltme yalnız akış adımındaki bir cevap için istenebilir.");

        var declinesFlagged = response.Status == FormResponseStatus.Flagged && contract.NewStatus == FormResponseStatus.Declined;

        response.ApplyReview(contract.NewStatus, reviewerId, contract.Note, _time.GetUtcNow().UtcDateTime);

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

        var isAuthorized = response.Form.Allows(archiverId, _currentUser.IsPlatformAdmin, FormAction.HandleResponses);

        if (!isAuthorized)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı arşivleme yetkiniz yok.");

        if (response.IsArchived)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Bu yanıt zaten arşivlenmiş.");

        if (response.Status == FormResponseStatus.Provisional)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Geçici cevap arşivlenemez; önce karar verin.");

        if (response.Status == FormResponseStatus.Returned)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Düzeltme istenen cevap başvurunun geçmişinde kalır; arşivlenemez.");

        var now = _time.GetUtcNow().UtcDateTime;

        if (response.Status is FormResponseStatus.Pending or FormResponseStatus.Flagged)
        {
            if (await _workflowRuntime.HasPendingRouteAsync(responseId, cancellationToken))
            {
                return new ServiceResult<bool>(
                    ServiceStatus.NotAcceptable,
                    Message: "Akış içindeki bekleyen bir cevap arşivlenemez; önce onaylayın veya reddedin.");
            }

            response.ApplyReview(FormResponseStatus.Declined, archiverId, "Arşivlendiği için sistem tarafından otomatik olarak reddedildi.", now);
        }

        response.Archive(archiverId, now);

        await _uow.SaveChangesAsync(cancellationToken);

        await _cache.TryRemoveAsync(FormCacheKeys.Analytics(response.FormId), cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Yanıt başarıyla arşivlendi.");
    }
}
