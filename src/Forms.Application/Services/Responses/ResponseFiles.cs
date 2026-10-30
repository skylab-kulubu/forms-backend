using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.Responses;

public interface IResponseFiles
{
    Task<ServiceResult<ResponseFileContract>> GetResponseFileAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResponseFileLinkContract>> CreateResponseFileLinkAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken = default);
}

public sealed class ResponseFiles : IResponseFiles
{
    private readonly IFormResponseRepository _responses;
    private readonly IAnswerFileStore _files;
    private readonly ICurrentUser _currentUser;
    private readonly IResponseShareLinks _shareLinks;

    public ResponseFiles(
        IFormResponseRepository responses,
        IAnswerFileStore files,
        ICurrentUser currentUser,
        IResponseShareLinks shareLinks)
    {
        _responses = responses;
        _files = files;
        _currentUser = currentUser;
        _shareLinks = shareLinks;
    }

    public async Task<ServiceResult<ResponseFileContract>> GetResponseFileAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        if (await CheckFileAccessAsync(responseId, mediaId, userId, token, cancellationToken) is { } denied)
            return new ServiceResult<ResponseFileContract>(denied.Status, Message: denied.Message);

        var read = await _files.FindAsync(mediaId, cancellationToken);

        if (read.Outcome == FileStoreOutcome.NotFound)
        {
            return new ServiceResult<ResponseFileContract>(
                ServiceStatus.Success,
                new ResponseFileContract(mediaId, null, null, null, ResponseFileStatus.Deleted, null, false, null));
        }

        if (read.Outcome != FileStoreOutcome.Ok || read.File is not { } media)
            return new ServiceResult<ResponseFileContract>(ServiceStatus.ServiceUnavailable, Message: "Dosya bilgisi şu an alınamıyor. Biraz sonra tekrar deneyin.");

        var isPrivate = media.IsPrivate;
        var status = media.State switch
        {
            FileState.Scanning => ResponseFileStatus.Scanning,
            FileState.Rejected => ResponseFileStatus.Rejected,
            _ => ResponseFileStatus.Ready
        };

        return new ServiceResult<ResponseFileContract>(
            ServiceStatus.Success,
            new ResponseFileContract(
                mediaId,
                media.Name,
                media.ContentType,
                media.Size,
                status,
                media.ScanResult,
                isPrivate,
                !isPrivate && !string.IsNullOrEmpty(media.PublicUrl) ? media.PublicUrl : null));
    }

    public async Task<ServiceResult<ResponseFileLinkContract>> CreateResponseFileLinkAsync(Guid responseId, Guid mediaId, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        if (await CheckFileAccessAsync(responseId, mediaId, userId, token, cancellationToken) is { } denied)
            return new ServiceResult<ResponseFileLinkContract>(denied.Status, Message: denied.Message);

        var link = await _files.CreateDownloadLinkAsync(mediaId, userId, cancellationToken);

        return link.Outcome switch
        {
            FileStoreOutcome.Ok when !string.IsNullOrEmpty(link.Url) => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.Success,
                new ResponseFileLinkContract(link.Url, link.ExpiresAt)),
            FileStoreOutcome.Scanning => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.Conflict,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Scanning, RetryAfterSeconds: link.RetryAfterSeconds),
                "Dosya hâlâ taranıyor. Biraz sonra tekrar deneyin."),
            FileStoreOutcome.Rejected => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.NotAvailable,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Rejected, ScanResult: link.ScanResult),
                "Dosya güvenlik taramasından geçmedi."),
            FileStoreOutcome.NotFound => new ServiceResult<ResponseFileLinkContract>(
                ServiceStatus.NotFound,
                new ResponseFileLinkContract(Reason: ResponseFileReason.Deleted),
                "Dosya artık mevcut değil."),
            FileStoreOutcome.SubjectInactive => new ServiceResult<ResponseFileLinkContract>(
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

        var canView = response.Form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.ViewResponses);

        if (!canView)
        {
            if (string.IsNullOrEmpty(token)) return (ServiceStatus.NotAuthorized, "Bu yanıtı görüntüleme yetkiniz yok.");

            if (await _shareLinks.FindGrantAsync(responseId, token, cancellationToken) is null)
                return (ServiceStatus.NotAuthorized, "Paylaşım bağlantısı geçersiz veya süresi dolmuş.");
        }

        var holdsFile = response.Data.Any(item =>
            item.Type == GuestUploadRules.FileQuestionType
            && Guid.TryParse(item.Answer, out var answer)
            && answer == mediaId);

        return holdsFile ? null : (ServiceStatus.NotFound, "Dosya bulunamadı.");
    }
}
