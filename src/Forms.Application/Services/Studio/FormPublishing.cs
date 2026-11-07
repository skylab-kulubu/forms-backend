using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Application.Services.Forms;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Application.Services.Studio;

public sealed record StagedPublication(
    Form Form,
    CollaboratorRole Role,
    FormWorkingCopy? Copy,
    long WorkingVersion,
    FormPublicationResult? Result
);

public interface IFormPublishing
{
    Task<ServiceResult<FormDocument>> PreviewAsync(Guid formId, long workingVersion, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StagedPublication>> StageAsync(Guid formId, long workingVersion, StudioCaller caller, CancellationToken ct = default);
    Task CompleteAsync(StagedPublication staged, Guid userId, CancellationToken ct = default);
    Task<FormContract> ContractOfAsync(Form form, CollaboratorRole role, CancellationToken ct = default);
}

public sealed class FormPublishing : IFormPublishing
{
    private const string ConflictMessage = "Siz kaydederken formda yeni değişiklikler oldu. Onları gördükten sonra tekrar kaydedin.";

    private readonly IFormRepository _forms;
    private readonly IFormStudioRepository _studio;
    private readonly IFormWorkflowRepository _workflows;
    private readonly IUserDirectory _users;
    private readonly IGuestUploadService _guestUploads;
    private readonly IFormDraftService _drafts;
    private readonly ICacheService _cache;
    private readonly IStudioNotifier _notifier;

    public FormPublishing(
        IFormRepository forms,
        IFormStudioRepository studio,
        IFormWorkflowRepository workflows,
        IUserDirectory users,
        IGuestUploadService guestUploads,
        IFormDraftService drafts,
        ICacheService cache,
        IStudioNotifier notifier)
    {
        _forms = forms;
        _studio = studio;
        _workflows = workflows;
        _users = users;
        _guestUploads = guestUploads;
        _drafts = drafts;
        _cache = cache;
        _notifier = notifier;
    }

    public async Task<ServiceResult<FormDocument>> PreviewAsync(Guid formId, long workingVersion, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (form is null) return new ServiceResult<FormDocument>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var copy = await _studio.FindWorkingCopyAsync(formId, ct);
        if (Refusal(form, caller, copy, workingVersion) is { } refusal) return new ServiceResult<FormDocument>(refusal.Status, Message: refusal.Message);

        return new ServiceResult<FormDocument>(ServiceStatus.Success, Data: DocumentOf(form, copy));
    }

    public async Task<ServiceResult<StagedPublication>> StageAsync(Guid formId, long workingVersion, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetForEditWithCollaboratorsAsync(formId, ct);
        if (form is null) return Fail(ServiceStatus.NotFound, "Form bulunamadı.");

        var role = form.RoleOf(caller.UserId);
        var copy = await _studio.FindWorkingCopyAsync(formId, ct);
        if (Refusal(form, caller, copy, workingVersion) is { } refusal) return Fail(refusal.Status, refusal.Message);

        var document = DocumentOf(form, copy);

        if (form.PublishedVersion > 0 && document.SameAs(FormDocument.Of(form)))
            return new ServiceResult<StagedPublication>(ServiceStatus.Success, Data: new StagedPublication(form, role, copy, workingVersion, null));

        var publication = await FormPublication.ApplyAsync(form, UpsertOf(document), _workflows, _guestUploads.Capability is not null, ct);
        if (publication.Failure is { } failure) return Fail(failure.Status, failure.Message);

        form.PublishedVersion += 1;

        return new ServiceResult<StagedPublication>(ServiceStatus.Success, Data: new StagedPublication(form, role, copy, workingVersion, publication));
    }

    public async Task CompleteAsync(StagedPublication staged, Guid userId, CancellationToken ct = default)
    {
        if (staged.Result is null) return;

        await FormPublication.AfterCommitAsync(staged.Form.Id, staged.Result, _drafts, _cache, ct);

        var published = FormDocument.Of(staged.Form);
        await _notifier.PublishedAsync(
            new StudioPublishedContract(staged.Form.Id, staged.Form.PublishedVersion, staged.WorkingVersion, userId, published.ToJson(), staged.Copy is not null && !staged.Copy.Document.SameAs(published)),
            ct);
    }

    public async Task<FormContract> ContractOfAsync(Form form, CollaboratorRole role, CancellationToken ct = default)
    {
        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _users.GetUsersAsync(collaboratorIds, ct);
        var membership = await FormEditorMapping.MembershipOfAsync(_workflows, form.Id, ct);

        return FormEditorMapping.ContractOf(form, users, role, FormEditorMapping.WorkflowRefOf(membership));
    }

    public static FormUpsertRequest UpsertOf(FormDocument document) => new(
        null,
        document.Title,
        document.Description,
        document.Schema,
        document.AllowAnonymousResponses,
        document.AllowMultipleResponses,
        document.RequiresManualReview,
        document.Status,
        null,
        document.Task,
        document.ClosesAt,
        document.TimeLimitMinutes);

    private static (ServiceStatus Status, string Message)? Refusal(Form form, StudioCaller caller, FormWorkingCopy? copy, long workingVersion)
    {
        if (!FormAccess.Allows(form.RoleOf(caller.UserId), caller.IsPlatformAdmin, FormAction.EditForm))
            return (ServiceStatus.NotAuthorized, "Bu formu kaydetme yetkiniz yok.");

        return (copy?.Version ?? 0) == workingVersion ? null : (ServiceStatus.Conflict, ConflictMessage);
    }

    private static FormDocument DocumentOf(Form form, FormWorkingCopy? copy) => (copy?.Document ?? FormDocument.Of(form)).Publishable();

    private static ServiceResult<StagedPublication> Fail(ServiceStatus status, string? message) => new(status, Message: message);
}
