using System.Net;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.GuestUploads;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.GuestUploads;

public class GuestUploadService : IGuestUploadService
{
    private const int UnavailableRetryAfterSeconds = 5;
    private const int ScanningRetryAfterSeconds = 5;
    private const long SecondsPerMinute = 60;
    private static readonly TimeSpan MinuteWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MinimumSessionWindow = TimeSpan.FromSeconds(1);
    private static readonly Refusal FormNotFound = new(ServiceStatus.NotFound, "Form bulunamadı.");

    private readonly IFormRepository _forms;
    private readonly ITurnstileVerifier _turnstile;
    private readonly IGuestUploadStore _store;
    private readonly ICoreMedia _media;
    private readonly GuestUploadOptions _options;

    public GuestUploadService(
        IFormRepository forms,
        ITurnstileVerifier turnstile,
        IGuestUploadStore store,
        ICoreMedia media,
        GuestUploadOptions options)
    {
        _forms = forms;
        _turnstile = turnstile;
        _store = store;
        _media = media;
        _options = options;
    }

    private bool IsActive => _options.Enabled && _turnstile.IsEnabled;

    public GuestUploadsContract? Capability =>
        IsActive ? new GuestUploadsContract(GuestUploadRules.MaxBytes, [.. GuestUploadRules.Types]) : null;

    public GuestUploadsContract? CapabilityFor(Form form) =>
        TakesSignedOutAnswers(form) ? Capability : null;

    public async Task<ServiceResult<GuestUploadSessionContract>> StartSessionAsync(Guid formId, string? turnstileToken, IPAddress? client, CancellationToken ct = default)
    {
        if (!IsActive) return ToSessionResult(Refuse(GuestUploadReason.GuestUploadsDisabled));

        var form = await _forms.GetByIdAsync(formId, ct);
        if (form is null) return ToSessionResult(FormNotFound);
        if (CheckForm(form) is { } formRefusal) return ToSessionResult(formRefusal);

        try
        {
            var (bucket, retryAfter) = MinuteBucket(DateTime.UtcNow);

            var sessions = await _store.CountAsync(GuestUploadKeys.IpSessions(GuestUploadKeys.ClientSubject(client), bucket), MinuteWindow, ct);
            if (sessions > _options.IpSessionsPerMinute) return ToSessionResult(Refuse(GuestUploadReason.TooManySessions, retryAfter));
        }
        catch (GuestUploadStoreUnavailableException)
        {
            return ToSessionResult(Refuse(GuestUploadReason.GuestUploadsUnavailable));
        }

        var verdict = await _turnstile.VerifyAsync(turnstileToken, TurnstileActions.GuestUpload, client, ct);
        if (verdict != TurnstileVerdict.Passed) return ToSessionResult(RefuseVerification(verdict));

        try
        {
            var now = DateTime.UtcNow;
            var session = new GuestUploadSession(GuestUploadKeys.NewSessionId(), formId, now, now + _options.SessionLifetime);
            await _store.SaveSessionAsync(session, ct);

            return new ServiceResult<GuestUploadSessionContract>(
                ServiceStatus.Success,
                new GuestUploadSessionContract(session.Id, session.ExpiresAt));
        }
        catch (GuestUploadStoreUnavailableException)
        {
            return ToSessionResult(Refuse(GuestUploadReason.GuestUploadsUnavailable));
        }
    }

    public async Task<ServiceResult<GuestUploadContract>> UploadAsync(Guid formId, string? sessionId, string? questionId, GuestFile file, IPAddress? client, CancellationToken ct = default)
    {
        if (!IsActive) return ToUploadResult(Refuse(GuestUploadReason.GuestUploadsDisabled));

        var form = await _forms.GetByIdAsync(formId, ct);
        if (form is null) return ToUploadResult(FormNotFound);
        if (CheckForm(form) is { } formRefusal) return ToUploadResult(formRefusal);

        try
        {
            var now = DateTime.UtcNow;
            var session = await FindSessionAsync(sessionId, formId, now, ct);
            if (session is null) return ToUploadResult(Refuse(GuestUploadReason.SessionExpired));

            var question = form.Schema.FirstOrDefault(item => GuestUploadRules.IsFileQuestion(item) && item.Id == questionId);
            if (question is null) return ToUploadResult(Refuse(GuestUploadReason.QuestionNotFound));

            var types = GuestUploadRules.EffectiveTypes(question);
            if (types.Count == 0) return ToUploadResult(Refuse(GuestUploadReason.FileTypeNotAllowed));

            if (file.Length <= 0) return ToUploadResult(Refuse(GuestUploadReason.FileEmpty));

            var maxBytes = GuestUploadRules.EffectiveMaxBytes(question);
            if (file.Length > maxBytes) return ToUploadResult(Refuse(GuestUploadReason.FileTooLarge, maxBytes: maxBytes));

            var name = GuestUploadRules.SanitizeFileName(file.FileName);
            if (!Allows(types, GuestUploadRules.TypeOfFileName(name)) && !Allows(types, GuestUploadRules.TypeOfContentType(file.ContentType)))
                return ToUploadResult(Refuse(GuestUploadReason.FileTypeNotAllowed));

            if (await CountUploadAsync(session, formId, client, now, ct) is { } limit) return ToUploadResult(limit);

            var upload = await _media.UploadGuestAnswerAsync(file.Content, name, file.ContentType, ct);
            if (upload.Outcome != CoreMediaOutcome.Ok || upload.Media is not { } media)
                return ToUploadResult(RefuseUpload(upload, maxBytes));

            var type = types.FirstOrDefault(allowed => string.Equals(allowed, media.Type, StringComparison.OrdinalIgnoreCase));
            if (type is null) return ToUploadResult(Refuse(GuestUploadReason.FileTypeNotAllowed));

            var stored = new GuestUploadedFile(media.Id, question.Id, type, file.Length, string.IsNullOrEmpty(media.Name) ? name : media.Name);
            await _store.SaveFileAsync(session.Id, stored, session.ExpiresAt, ct);

            return new ServiceResult<GuestUploadContract>(ServiceStatus.Success, Describe(stored, NormalizeStatus(media.Status), media.ScanResult));
        }
        catch (GuestUploadStoreUnavailableException)
        {
            return ToUploadResult(Refuse(GuestUploadReason.GuestUploadsUnavailable));
        }
    }

    public async Task<ServiceResult<GuestUploadContract>> GetStatusAsync(Guid formId, string? sessionId, Guid mediaId, CancellationToken ct = default)
    {
        if (!IsActive) return ToUploadResult(Refuse(GuestUploadReason.GuestUploadsDisabled));

        GuestUploadedFile? file;
        try
        {
            var session = await FindSessionAsync(sessionId, formId, DateTime.UtcNow, ct);
            if (session is null) return ToUploadResult(Refuse(GuestUploadReason.SessionExpired));

            file = await _store.FindFileAsync(session.Id, mediaId, ct);
        }
        catch (GuestUploadStoreUnavailableException)
        {
            return ToUploadResult(Refuse(GuestUploadReason.GuestUploadsUnavailable));
        }

        if (file is null) return ToUploadResult(Refuse(GuestUploadReason.FileExpired) with { Status = ServiceStatus.NotFound });

        var read = await _media.GetAsync(mediaId, ct);
        if (read.Outcome == CoreMediaOutcome.NotFound)
            return new ServiceResult<GuestUploadContract>(ServiceStatus.Success, Describe(file, GuestUploadStatus.Missing, null));

        if (read.Outcome != CoreMediaOutcome.Ok || read.Media is not { } media)
            return ToUploadResult(Refuse(GuestUploadReason.GuestUploadsUnavailable));

        return new ServiceResult<GuestUploadContract>(ServiceStatus.Success, Describe(file, NormalizeStatus(media.Status), media.ScanResult));
    }

    public async Task<GuestSubmitGate> CheckSubmitAsync(Form form, ResponseSubmitRequest request, IPAddress? client, CancellationToken ct = default)
    {
        var subject = GuestUploadKeys.ClientSubject(client);
        if (await CountSubmitAsync(subject, form.Id, ct) is { } busy) return Reject(busy);

        var verdict = await _turnstile.VerifyAsync(request.TurnstileToken, TurnstileActions.GuestSubmit, client, ct);
        if (verdict == TurnstileVerdict.Rejected) return Reject(Refuse(GuestUploadReason.VerificationFailed));

        var verified = verdict != TurnstileVerdict.Unavailable;
        if (!verified && await CountUnverifiedSubmitAsync(subject, form.Id, ct) is { } held) return Reject(held);

        var answers = form.Schema
            .Where(GuestUploadRules.IsFileQuestion)
            .Select(question => (Question: question, Answer: request.Responses?.FirstOrDefault(response => response.Id == question.Id)?.Answer))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Answer))
            .ToList();

        if (answers.Count == 0) return verified ? GuestSubmitGate.Pass : GuestSubmitGate.PassUnverified;

        var firstQuestionId = answers[0].Question.Id;
        if (!IsActive) return Reject(Refuse(GuestUploadReason.GuestUploadsDisabled, questionId: firstQuestionId));

        try
        {
            var session = await FindSessionAsync(request.GuestUploadSession, form.Id, DateTime.UtcNow, ct);
            if (session is null) return Reject(Refuse(GuestUploadReason.FileExpired, questionId: firstQuestionId));

            var files = new List<(FormSchemaItem Question, GuestUploadedFile File)>(answers.Count);
            foreach (var (question, answer) in answers)
            {
                var file = Guid.TryParse(answer, out var mediaId) ? await _store.FindFileAsync(session.Id, mediaId, ct) : null;
                if (file is null || file.QuestionId != question.Id)
                    return Reject(Refuse(GuestUploadReason.FileExpired, questionId: question.Id));

                files.Add((question, file));
            }

            foreach (var (question, file) in files)
            {
                if (await CheckSubmittedFileAsync(question, file, ct) is { } refusal) return Reject(refusal);
            }

            return new GuestSubmitGate(null, session.Id, [.. files.Select(pair => new GuestSubmitFile(pair.Question.Id, pair.File.MediaId))], verified);
        }
        catch (GuestUploadStoreUnavailableException)
        {
            return Reject(Refuse(GuestUploadReason.GuestUploadsUnavailable));
        }
    }

    public async Task<GuestSubmitGate> CheckAccountSubmitAsync(Form form, ResponseSubmitRequest request, Guid userId, CancellationToken ct = default)
    {
        var files = new List<GuestSubmitFile>();

        foreach (var question in form.Schema.Where(GuestUploadRules.IsFileQuestion))
        {
            var answer = request.Responses?.FirstOrDefault(response => response.Id == question.Id)?.Answer;
            if (!Guid.TryParse(answer, out var mediaId)) continue;

            var read = await _media.GetAsync(mediaId, ct);
            if (read.Outcome == CoreMediaOutcome.NotFound) return Reject(Refuse(GuestUploadReason.FileExpired, questionId: question.Id));
            if (read.Outcome != CoreMediaOutcome.Ok || read.Media is not { } media) return Reject(Refuse(GuestUploadReason.GuestUploadsUnavailable));

            if (string.Equals(media.Purpose, CoreMediaPurpose.Legacy, StringComparison.Ordinal)) continue;

            if (CheckAccountFile(question, media, userId) is { } refusal) return Reject(refusal);

            files.Add(new GuestSubmitFile(question.Id, mediaId));
        }

        return new GuestSubmitGate(null, null, files);
    }

    public async Task<GuestAttachResult> AttachAsync(IReadOnlyList<GuestSubmitFile> files, Guid responseId, Guid? onBehalfOf, CancellationToken ct = default)
    {
        var attached = new List<GuestAttachment>(files.Count);

        try
        {
            foreach (var file in files)
            {
                var result = await _media.AttachToResponseAsync(file.MediaId, responseId, onBehalfOf, ct);
                if (result is { Outcome: CoreMediaOutcome.Ok, AttachmentId: { } attachmentId })
                {
                    attached.Add(new GuestAttachment(file.MediaId, attachmentId));
                    continue;
                }

                await DetachAsync(attached);

                var refusal = result.Outcome == CoreMediaOutcome.NotLinkable
                    ? Refuse(GuestUploadReason.FileExpired, questionId: file.QuestionId)
                    : Refuse(GuestUploadReason.GuestUploadsUnavailable);

                return new GuestAttachResult(ToSubmitResult(refusal), []);
            }
        }
        catch
        {
            await DetachAsync(attached);
            throw;
        }

        return new GuestAttachResult(null, attached);
    }

    public async Task DetachAsync(IReadOnlyList<GuestAttachment> attachments)
    {
        foreach (var attachment in attachments)
        {
            try { await _media.DetachAsync(attachment.MediaId, attachment.AttachmentId, CancellationToken.None); }
            catch (Exception) { }
        }
    }

    public async Task ConsumeAsync(string sessionId, IReadOnlyList<GuestSubmitFile> files)
    {
        if (files.Count == 0) return;

        try { await _store.RemoveFilesAsync(sessionId, [.. files.Select(file => file.MediaId)], CancellationToken.None); }
        catch (GuestUploadStoreUnavailableException) { }
    }

    private Refusal? CheckForm(Form form)
    {
        if (form.Status == FormStatus.Deleted) return FormNotFound;

        if (!TakesSignedOutAnswers(form) || !form.Schema.Any(GuestUploadRules.IsFileQuestion))
            return Refuse(GuestUploadReason.GuestUploadsDisabled);

        if (form.Status != FormStatus.Open)
            return new Refusal(ServiceStatus.NotAvailable, "Bu form şu anda yanıt kabul etmiyor.", FormClosedReason.Closed);

        if (!form.HasTimeLimit && form.HasClosedAt(DateTime.UtcNow))
            return new Refusal(ServiceStatus.NotAvailable, "Bu form kapanış saatinde kendiliğinden kapandı.", FormClosedReason.Closed);

        return null;
    }

    private async Task<GuestUploadSession?> FindSessionAsync(string? sessionId, Guid formId, DateTime now, CancellationToken ct)
    {
        if (!GuestUploadKeys.IsSessionId(sessionId)) return null;

        var session = await _store.FindSessionAsync(sessionId, ct);

        return session is not null
            && string.Equals(session.Id, sessionId, StringComparison.Ordinal)
            && session.FormId == formId
            && session.ExpiresAt > now
                ? session
                : null;
    }

    private async Task<Refusal?> CountUploadAsync(GuestUploadSession session, Guid formId, IPAddress? client, DateTime now, CancellationToken ct)
    {
        var remaining = session.ExpiresAt - now;
        var sessionWindow = remaining > MinimumSessionWindow ? remaining : MinimumSessionWindow;

        var sessionUploads = await _store.CountAsync(GuestUploadKeys.SessionCount(session.Id), sessionWindow, ct);
        if (sessionUploads > _options.SessionMaxFiles) return Refuse(GuestUploadReason.SessionFileLimit);

        var (bucket, retryAfter) = MinuteBucket(now);

        var clientUploads = await _store.CountAsync(GuestUploadKeys.IpFiles(GuestUploadKeys.ClientSubject(client), bucket), MinuteWindow, ct);
        if (clientUploads > _options.IpFilesPerMinute) return Refuse(GuestUploadReason.TooManyUploads, retryAfter);

        var formUploads = await _store.CountAsync(GuestUploadKeys.FormFiles(formId, bucket), MinuteWindow, ct);
        if (formUploads > _options.FormFilesPerMinute) return Refuse(GuestUploadReason.TooManyUploads, retryAfter);

        return null;
    }

    private async Task<Refusal?> CountSubmitAsync(string subject, Guid formId, CancellationToken ct)
    {
        var (bucket, retryAfter) = MinuteBucket(DateTime.UtcNow);

        try
        {
            var fromClient = await _store.CountAsync(GuestUploadKeys.IpSubmits(subject, bucket), MinuteWindow, ct);
            if (fromClient > _options.IpSubmitsPerMinute) return Refuse(GuestUploadReason.TooManySubmissions, retryAfter);

            var toForm = await _store.CountAsync(GuestUploadKeys.FormSubmits(formId, bucket), MinuteWindow, ct);
            if (toForm > _options.FormSubmitsPerMinute) return Refuse(GuestUploadReason.TooManySubmissions, retryAfter);
        }
        catch (GuestUploadStoreUnavailableException) { }

        return null;
    }

    private async Task<Refusal?> CountUnverifiedSubmitAsync(string subject, Guid formId, CancellationToken ct)
    {
        var (bucket, retryAfter) = MinuteBucket(DateTime.UtcNow);

        try
        {
            var fromClient = await _store.CountAsync(GuestUploadKeys.UnverifiedIpSubmits(subject, bucket), MinuteWindow, ct);
            if (fromClient > _options.UnverifiedIpSubmitsPerMinute) return Refuse(GuestUploadReason.TooManySubmissions, retryAfter);

            var toForm = await _store.CountAsync(GuestUploadKeys.UnverifiedFormSubmits(formId, bucket), MinuteWindow, ct);
            if (toForm > _options.UnverifiedFormSubmitsPerMinute) return Refuse(GuestUploadReason.TooManySubmissions, retryAfter);

            var total = await _store.CountAsync(GuestUploadKeys.UnverifiedSubmits(bucket), MinuteWindow, ct);
            if (total > _options.UnverifiedSubmitsPerMinute) return Refuse(GuestUploadReason.TooManySubmissions, retryAfter);
        }
        catch (GuestUploadStoreUnavailableException)
        {
            return Refuse(GuestUploadReason.SubmitUnavailable);
        }

        return null;
    }

    private async Task<Refusal?> CheckSubmittedFileAsync(FormSchemaItem question, GuestUploadedFile file, CancellationToken ct)
    {
        var read = await _media.GetAsync(file.MediaId, ct);
        if (read.Outcome == CoreMediaOutcome.NotFound) return Refuse(GuestUploadReason.FileExpired, questionId: question.Id);
        if (read.Outcome != CoreMediaOutcome.Ok || read.Media is not { } media) return Refuse(GuestUploadReason.GuestUploadsUnavailable);

        return media.Status switch
        {
            CoreMediaStatus.Scanning => Refuse(GuestUploadReason.FileScanning, ScanningRetryAfterSeconds, question.Id),
            CoreMediaStatus.Rejected => Refuse(GuestUploadReason.FileRejected, questionId: question.Id, scanResult: media.ScanResult),
            CoreMediaStatus.Attached or CoreMediaStatus.Detached => Refuse(GuestUploadReason.FileExpired, questionId: question.Id),
            CoreMediaStatus.Pending => CheckPendingFile(question, file, media),
            _ => Refuse(GuestUploadReason.GuestUploadsUnavailable)
        };
    }

    private Refusal? CheckPendingFile(FormSchemaItem question, GuestUploadedFile file, CoreMedia media)
    {
        var allowed = string.Equals(media.Purpose, CoreMediaPurpose.AnswerFileGuest, StringComparison.Ordinal)
            && media.Type is { } type
            && GuestUploadRules.EffectiveTypes(question).Contains(type, StringComparer.OrdinalIgnoreCase);
        if (!allowed) return Refuse(GuestUploadReason.FileTypeNotAllowed, questionId: question.Id);

        var maxBytes = GuestUploadRules.EffectiveMaxBytes(question);
        return file.Size > maxBytes ? Refuse(GuestUploadReason.FileTooLarge, questionId: question.Id, maxBytes: maxBytes) : null;
    }

    private Refusal? CheckAccountFile(FormSchemaItem question, CoreMedia media, Guid userId)
    {
        if (!string.Equals(media.Purpose, CoreMediaPurpose.AnswerFile, StringComparison.Ordinal) || media.UploadedBy != userId)
            return Refuse(GuestUploadReason.FileExpired, questionId: question.Id);

        return media.Status switch
        {
            CoreMediaStatus.Scanning => Refuse(GuestUploadReason.FileScanning, ScanningRetryAfterSeconds, question.Id),
            CoreMediaStatus.Rejected => Refuse(GuestUploadReason.FileRejected, questionId: question.Id, scanResult: media.ScanResult),
            CoreMediaStatus.Pending or CoreMediaStatus.Attached or CoreMediaStatus.Detached => null,
            _ => Refuse(GuestUploadReason.GuestUploadsUnavailable)
        };
    }

    private Refusal RefuseVerification(TurnstileVerdict verdict) => verdict switch
    {
        TurnstileVerdict.Rejected => Refuse(GuestUploadReason.VerificationFailed),
        TurnstileVerdict.Unavailable => Refuse(GuestUploadReason.GuestUploadsUnavailable),
        _ => Refuse(GuestUploadReason.GuestUploadsDisabled)
    };

    private Refusal RefuseUpload(CoreMediaUpload upload, long maxBytes) => upload.Outcome switch
    {
        CoreMediaOutcome.TooLarge => Refuse(GuestUploadReason.FileTooLarge, maxBytes: maxBytes),
        CoreMediaOutcome.TypeNotAllowed => Refuse(GuestUploadReason.FileTypeNotAllowed),
        CoreMediaOutcome.NameInvalid => Refuse(GuestUploadReason.FileNameInvalid),
        CoreMediaOutcome.RateLimited => Refuse(GuestUploadReason.TooManyUploads, upload.RetryAfterSeconds),
        _ => Refuse(GuestUploadReason.GuestUploadsUnavailable, upload.RetryAfterSeconds)
    };

    private Refusal Refuse(string reason, int? retryAfterSeconds = null, string? questionId = null, string? scanResult = null, long? maxBytes = null)
    {
        var (status, message) = reason switch
        {
            GuestUploadReason.VerificationFailed => (ServiceStatus.NotAcceptable, "Güvenlik doğrulaması geçmedi. Lütfen tekrar deneyin."),
            GuestUploadReason.GuestUploadsDisabled => (ServiceStatus.NotAuthorized, "Bu forma giriş yapmadan dosya yüklenemiyor."),
            GuestUploadReason.GuestUploadsUnavailable => (ServiceStatus.ServiceUnavailable, "Dosya yükleme şu an kullanılamıyor. Biraz sonra tekrar deneyin."),
            GuestUploadReason.SessionExpired => (ServiceStatus.NotAvailable, "Yükleme oturumunun süresi doldu."),
            GuestUploadReason.TooManySessions or GuestUploadReason.TooManyUploads => (ServiceStatus.TooManyRequests, "Kısa sürede çok fazla dosya yüklendi. Biraz sonra tekrar deneyin."),
            GuestUploadReason.SessionFileLimit => (ServiceStatus.TooManyRequests, $"Bu formda bir seferde en fazla {_options.SessionMaxFiles} dosya yüklenebilir."),
            GuestUploadReason.QuestionNotFound => (ServiceStatus.NotAcceptable, "Dosya sorusu bulunamadı."),
            GuestUploadReason.FileEmpty => (ServiceStatus.NotAcceptable, "Dosya boş."),
            GuestUploadReason.FileTooLarge => (ServiceStatus.NotAcceptable, "Dosya boyutu sınırı aşıldı."),
            GuestUploadReason.FileTypeNotAllowed => (ServiceStatus.NotAcceptable, "Bu dosya türüne izin verilmiyor."),
            GuestUploadReason.FileNameInvalid => (ServiceStatus.NotAcceptable, "Dosya adı geçersiz karakterler içeriyor."),
            GuestUploadReason.FileScanning => (ServiceStatus.Conflict, "Dosyanız hâlâ taranıyor. Birkaç saniye sonra tekrar gönderin."),
            GuestUploadReason.FileRejected => (ServiceStatus.NotAcceptable, "Dosyanız güvenlik taramasından geçmedi. Başka bir dosya yükleyin."),
            GuestUploadReason.FileExpired => (ServiceStatus.NotAvailable, "Yüklediğiniz dosyanın süresi doldu. Dosyayı yeniden yükleyin."),
            GuestUploadReason.TooManySubmissions => (ServiceStatus.TooManyRequests, "Şu an çok yoğun. Birkaç saniye sonra tekrar gönderin."),
            GuestUploadReason.SubmitUnavailable => (ServiceStatus.ServiceUnavailable, "Şu an gönderilemiyor. Biraz sonra tekrar deneyin."),
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };

        if (reason is GuestUploadReason.GuestUploadsUnavailable or GuestUploadReason.SubmitUnavailable) retryAfterSeconds ??= UnavailableRetryAfterSeconds;

        return new Refusal(status, message, reason, retryAfterSeconds, questionId, scanResult, maxBytes);
    }

    private static GuestSubmitGate Reject(Refusal refusal) => GuestSubmitGate.Reject(ToSubmitResult(refusal));

    private static ServiceResult<GuestUploadSessionContract> ToSessionResult(Refusal refusal) =>
        new(refusal.Status,
            refusal.Reason is null ? null : new GuestUploadSessionContract(Reason: refusal.Reason, RetryAfterSeconds: refusal.RetryAfterSeconds),
            refusal.Message);

    private static ServiceResult<GuestUploadContract> ToUploadResult(Refusal refusal) =>
        new(refusal.Status,
            refusal.Reason is null
                ? null
                : new GuestUploadContract(ScanResult: refusal.ScanResult, Reason: refusal.Reason, RetryAfterSeconds: refusal.RetryAfterSeconds, MaxBytes: refusal.MaxBytes),
            refusal.Message);

    private static ServiceResult<ResponseSubmitResult> ToSubmitResult(Refusal refusal) =>
        new(refusal.Status,
            new ResponseSubmitResult(
                null,
                null,
                0,
                Reason: refusal.Reason,
                QuestionId: refusal.QuestionId,
                ScanResult: refusal.ScanResult,
                RetryAfterSeconds: refusal.RetryAfterSeconds),
            refusal.Message);

    private static GuestUploadContract Describe(GuestUploadedFile file, string status, string? scanResult) =>
        new(file.MediaId, file.Name, file.Type, file.Size, status, scanResult);

    private static string NormalizeStatus(string? status) => status switch
    {
        CoreMediaStatus.Pending or CoreMediaStatus.Attached or CoreMediaStatus.Detached => GuestUploadStatus.Ready,
        CoreMediaStatus.Rejected => GuestUploadStatus.Rejected,
        _ => GuestUploadStatus.Scanning
    };

    private static bool Allows(IReadOnlyList<string> types, string? type) =>
        type is not null && types.Contains(type);

    private static bool TakesSignedOutAnswers(Form form) => form.AllowAnonymousResponses;

    private static (long Bucket, int RetryAfterSeconds) MinuteBucket(DateTime now)
    {
        var unixSeconds = new DateTimeOffset(now).ToUnixTimeSeconds();
        var bucket = unixSeconds / SecondsPerMinute;
        return (bucket, (int)((bucket + 1) * SecondsPerMinute - unixSeconds));
    }

    private sealed record Refusal(
        ServiceStatus Status,
        string Message,
        string? Reason = null,
        int? RetryAfterSeconds = null,
        string? QuestionId = null,
        string? ScanResult = null,
        long? MaxBytes = null);
}
