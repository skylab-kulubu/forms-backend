using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Application.Services.Studio;

public interface IFormComments
{
    Task<ServiceResult<List<StudioThreadContract>>> ListAsync(Guid formId, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioThreadContract>> StartAsync(Guid formId, StudioThreadRequest request, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioThreadContract>> ReplyAsync(Guid formId, Guid threadId, StudioCommentRequest request, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioThreadContract>> EditAsync(Guid formId, Guid commentId, StudioCommentRequest request, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioThreadContract>> DeleteAsync(Guid formId, Guid commentId, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioThreadContract>> ResolveAsync(Guid formId, Guid threadId, bool resolved, StudioCaller caller, CancellationToken ct = default);
}

public sealed class FormComments : IFormComments
{
    private readonly IFormRepository _forms;
    private readonly IFormStudioRepository _studio;
    private readonly IFormsUnitOfWork _uow;
    private readonly IUserDirectory _users;
    private readonly IStudioNotifier _notifier;
    private readonly TimeProvider _time;

    public FormComments(
        IFormRepository forms,
        IFormStudioRepository studio,
        IFormsUnitOfWork uow,
        IUserDirectory users,
        IStudioNotifier notifier,
        TimeProvider time)
    {
        _forms = forms;
        _studio = studio;
        _uow = uow;
        _users = users;
        _notifier = notifier;
        _time = time;
    }

    public async Task<ServiceResult<List<StudioThreadContract>>> ListAsync(Guid formId, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (form is null) return Fail<List<StudioThreadContract>>(ServiceStatus.NotFound, "Form bulunamadı.");

        if (!FormAccess.Allows(form.RoleOf(caller.UserId), caller.IsPlatformAdmin, FormAction.OpenEditor))
            return Fail<List<StudioThreadContract>>(ServiceStatus.NotAuthorized, "Bu formun yorumlarını görme yetkiniz yok.");

        var comments = await _studio.GetCommentsAsync(formId, ct);
        var users = await UsersOfAsync(comments, ct);

        var threads = comments
            .Where(comment => comment.ThreadId is null)
            .Select(root => ThreadOf(root, comments.Where(comment => comment.ThreadId == root.Id), users))
            .ToList();

        return new ServiceResult<List<StudioThreadContract>>(ServiceStatus.Success, threads);
    }

    public async Task<ServiceResult<StudioThreadContract>> StartAsync(Guid formId, StudioThreadRequest request, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (RefusalOf(form, caller) is { } refusal) return refusal;
        if (BodyError(request.Body) is { } error) return Fail<StudioThreadContract>(ServiceStatus.NotAcceptable, error);

        var copy = await _studio.FindWorkingCopyAsync(formId, ct);
        var document = copy?.Document ?? FormDocument.Of(form!);

        if (string.IsNullOrEmpty(request.FieldId) || !document.HasField(request.FieldId))
            return Fail<StudioThreadContract>(ServiceStatus.NotAcceptable, "Bu soru formda bulunmuyor.");

        var root = new FormComment
        {
            FormId = formId,
            FieldId = request.FieldId,
            AuthorId = caller.UserId,
            Body = request.Body.Trim(),
            CreatedAt = _time.GetUtcNow().UtcDateTime,
        };

        _studio.Add(root);
        await _uow.SaveChangesAsync(ct);

        return await AnnounceAsync(formId, root.Id, ct);
    }

    public async Task<ServiceResult<StudioThreadContract>> ReplyAsync(Guid formId, Guid threadId, StudioCommentRequest request, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (RefusalOf(form, caller) is { } refusal) return refusal;
        if (BodyError(request.Body) is { } error) return Fail<StudioThreadContract>(ServiceStatus.NotAcceptable, error);

        var root = await _studio.GetCommentAsync(formId, threadId, ct);
        if (root is not { ThreadId: null }) return Fail<StudioThreadContract>(ServiceStatus.NotFound, "Yorum bulunamadı.");
        if (root.ResolvedAt is not null) return Fail<StudioThreadContract>(ServiceStatus.NotAcceptable, "Çözülmüş yoruma cevap yazılamaz; önce yeniden açın.");

        _studio.Add(new FormComment
        {
            FormId = formId,
            FieldId = root.FieldId,
            ThreadId = root.Id,
            AuthorId = caller.UserId,
            Body = request.Body.Trim(),
            CreatedAt = _time.GetUtcNow().UtcDateTime,
        });
        await _uow.SaveChangesAsync(ct);

        return await AnnounceAsync(formId, root.Id, ct);
    }

    public async Task<ServiceResult<StudioThreadContract>> EditAsync(Guid formId, Guid commentId, StudioCommentRequest request, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (RefusalOf(form, caller) is { } refusal) return refusal;
        if (BodyError(request.Body) is { } error) return Fail<StudioThreadContract>(ServiceStatus.NotAcceptable, error);

        var comment = await _studio.GetCommentAsync(formId, commentId, ct);
        if (comment is null) return Fail<StudioThreadContract>(ServiceStatus.NotFound, "Yorum bulunamadı.");
        if (comment.AuthorId != caller.UserId) return Fail<StudioThreadContract>(ServiceStatus.NotAuthorized, "Yalnız kendi yorumunuzu düzenleyebilirsiniz.");

        comment.Body = request.Body.Trim();
        comment.EditedAt = _time.GetUtcNow().UtcDateTime;
        await _uow.SaveChangesAsync(ct);

        return await AnnounceAsync(formId, comment.ThreadId ?? comment.Id, ct);
    }

    public async Task<ServiceResult<StudioThreadContract>> DeleteAsync(Guid formId, Guid commentId, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (RefusalOf(form, caller) is { } refusal) return refusal;

        var comment = await _studio.GetCommentAsync(formId, commentId, ct);
        if (comment is null) return Fail<StudioThreadContract>(ServiceStatus.NotFound, "Yorum bulunamadı.");

        if (comment.AuthorId != caller.UserId && form!.RoleOf(caller.UserId) != CollaboratorRole.Owner)
            return Fail<StudioThreadContract>(ServiceStatus.NotAuthorized, "Yalnız kendi yorumunuzu silebilirsiniz.");

        var threadId = comment.ThreadId ?? comment.Id;
        var fieldId = comment.FieldId;

        _studio.Remove(comment);
        await _uow.SaveChangesAsync(ct);

        if (comment.ThreadId is not null) return await AnnounceAsync(formId, threadId, ct);

        var removed = new StudioThreadContract(threadId, fieldId, null, null, []);
        await _notifier.CommentsChangedAsync(formId, removed, ct);

        return new ServiceResult<StudioThreadContract>(ServiceStatus.Success, removed);
    }

    public async Task<ServiceResult<StudioThreadContract>> ResolveAsync(Guid formId, Guid threadId, bool resolved, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (RefusalOf(form, caller) is { } refusal) return refusal;

        var root = await _studio.GetCommentAsync(formId, threadId, ct);
        if (root is not { ThreadId: null }) return Fail<StudioThreadContract>(ServiceStatus.NotFound, "Yorum bulunamadı.");

        root.ResolvedAt = resolved ? _time.GetUtcNow().UtcDateTime : null;
        root.ResolvedBy = resolved ? caller.UserId : null;
        await _uow.SaveChangesAsync(ct);

        return await AnnounceAsync(formId, root.Id, ct);
    }

    private static ServiceResult<StudioThreadContract>? RefusalOf(Form? form, StudioCaller caller)
    {
        if (form is null) return Fail<StudioThreadContract>(ServiceStatus.NotFound, "Form bulunamadı.");

        return FormAccess.Allows(form.RoleOf(caller.UserId), caller.IsPlatformAdmin, FormAction.EditForm)
            ? null
            : Fail<StudioThreadContract>(ServiceStatus.NotAuthorized, "Bu forma yorum yazma yetkiniz yok.");
    }

    private static string? BodyError(string? body)
    {
        var text = body?.Trim() ?? string.Empty;
        if (text.Length == 0) return "Yorum boş olamaz.";

        return text.Length > StudioLimits.MaxCommentLength ? $"Yorum en fazla {StudioLimits.MaxCommentLength} karakter olabilir." : null;
    }

    private async Task<ServiceResult<StudioThreadContract>> AnnounceAsync(Guid formId, Guid threadId, CancellationToken ct)
    {
        var comments = await _studio.GetThreadAsync(formId, threadId, ct);
        var root = comments.First(comment => comment.Id == threadId);
        var users = await UsersOfAsync(comments, ct);
        var thread = ThreadOf(root, comments.Where(comment => comment.Id != threadId), users);

        await _notifier.CommentsChangedAsync(formId, thread, ct);

        return new ServiceResult<StudioThreadContract>(ServiceStatus.Success, thread);
    }

    private async Task<List<UserContract>> UsersOfAsync(List<FormComment> comments, CancellationToken ct)
    {
        var ids = comments.Select(comment => comment.AuthorId)
            .Concat(comments.Where(comment => comment.ResolvedBy is not null).Select(comment => comment.ResolvedBy!.Value))
            .Distinct()
            .ToList();

        return ids.Count == 0 ? [] : await _users.GetUsersAsync(ids, ct);
    }

    private static StudioThreadContract ThreadOf(FormComment root, IEnumerable<FormComment> replies, List<UserContract> users) => new(
        root.Id,
        root.FieldId,
        root.ResolvedAt,
        root.ResolvedBy is { } resolver ? UserOf(resolver, users) : null,
        [.. new[] { root }.Concat(replies.OrderBy(reply => reply.CreatedAt)).Select(comment => new StudioCommentContract(comment.Id, UserOf(comment.AuthorId, users), comment.Body, comment.CreatedAt, comment.EditedAt))]);

    private static UserContract UserOf(Guid userId, List<UserContract> users) =>
        users.FirstOrDefault(user => user.Id == userId) ?? new UserContract(userId, null, null, null);

    private static ServiceResult<T> Fail<T>(ServiceStatus status, string message) => new(status, Message: message);
}
