using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Application.Validators;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Application.Services.Forms;

public interface IFormAuthoring
{
    Task<ServiceResult<FormContract>> CreateFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormContract>> UpdateFormAsync(Guid formId, FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<List<FormCollaboratorContract>>> UpdateCollaboratorsAsync(Guid formId, FormCollaboratorsRequest request, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> RestoreFormAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class FormAuthoring : IFormAuthoring
{
    private readonly IFormRepository _forms;
    private readonly IFormsUnitOfWork _uow;
    private readonly IUserDirectory _userService;
    private readonly IFormDraftService _draftService;
    private readonly ICurrentUser _currentUser;
    private readonly ICacheService _cache;
    private readonly IFormWorkflowRepository _workflows;
    private readonly IGuestUploadService _guestUploads;
    private readonly IFormStudioRepository _studio;
    private readonly IStudioNotifier _notifier;
    private readonly TimeProvider _time;

    public FormAuthoring(
        IFormRepository forms,
        IFormsUnitOfWork uow,
        IUserDirectory userService,
        IFormDraftService draftService,
        ICurrentUser currentUser,
        ICacheService cache,
        IFormWorkflowRepository workflows,
        IGuestUploadService guestUploads,
        IFormStudioRepository studio,
        IStudioNotifier notifier,
        TimeProvider time)
    {
        _forms = forms;
        _uow = uow;
        _userService = userService;
        _draftService = draftService;
        _currentUser = currentUser;
        _cache = cache;
        _workflows = workflows;
        _guestUploads = guestUploads;
        _studio = studio;
        _notifier = notifier;
        _time = time;
    }

    public async Task<ServiceResult<FormContract>> CreateFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var schema = contract.Schema ?? new();

        var validation = FormValidator.ValidateUpsert(contract, schema, _guestUploads.Capability is not null);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var task = FormPublication.NormalizeTask(contract.Task);
        var timeLimit = FormPublication.NormalizeTimeLimit(contract.TimeLimitMinutes);

        var timing = FormValidator.ValidateTiming(contract.AllowAnonymousResponses, contract.AllowMultipleResponses, task, timeLimit);
        if (timing.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(timing.Status, Message: timing.Message);

        var formId = Guid.NewGuid();

        var newForm = new Form
        {
            Id = formId,
            Title = contract.Title,
            Description = contract.Description,
            Schema = schema,
            Status = contract.Status,
            AllowAnonymousResponses = contract.AllowAnonymousResponses,
            AllowMultipleResponses = contract.AllowMultipleResponses,
            RequiresManualReview = contract.RequiresManualReview,
            Task = task,
            ClosesAt = UtcDate.Normalize(contract.ClosesAt),
            TimeLimitMinutes = timeLimit,
            Brand = contract.Brand
        };

        var collaborators = new List<FormCollaborator>
        {
            new FormCollaborator { FormId = formId, UserId = userId, Role = CollaboratorRole.Owner }
        };

        if (contract.Collaborators != null)
        {
            foreach (var incoming in contract.Collaborators)
            {
                if (incoming.UserId == userId) continue;

                var safeRole = incoming.Role == CollaboratorRole.Owner ? CollaboratorRole.Editor : incoming.Role;
                collaborators.Add(new FormCollaborator { FormId = formId, UserId = incoming.UserId, Role = safeRole });
            }
        }

        newForm.Collaborators = collaborators;
        _forms.Add(newForm);

        await _uow.SaveChangesAsync(cancellationToken);

        var collaboratorIds = newForm.Collaborators.Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(ServiceStatus.Success, Data: FormEditorMapping.ContractOf(newForm, users, CollaboratorRole.Owner));
    }

    public async Task<ServiceResult<FormContract>> UpdateFormAsync(Guid formId, FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var existingForm = await _forms.GetForEditWithCollaboratorsAsync(formId, cancellationToken);
        if (existingForm == null)
            return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var role = existingForm.RoleOf(userId);
        if (!FormAccess.Allows(role, _currentUser.IsPlatformAdmin, FormAction.EditForm))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Bu formu düzenleme yetkiniz yok.");

        var copy = await _studio.GetWorkingCopyAsync(formId, cancellationToken);
        if (copy is not null && !copy.Document.SameAs(FormDocument.Of(existingForm)))
            return new ServiceResult<FormContract>(ServiceStatus.Conflict, Message: "Bu formun stüdyoda kaydedilmemiş değişiklikleri var; formu stüdyodan kaydedin.");

        var publication = await FormPublication.ApplyAsync(existingForm, contract, _workflows, _guestUploads.Capability is not null, cancellationToken);
        if (publication.Failure is { } failure) return failure;

        if (contract.Collaborators != null)
        {
            var incomingCollaborators = contract.Collaborators.Select(c => (c.UserId, c.Role));
            var currentUser = (userId, role);

            existingForm.UpdateCollaborators(incomingCollaborators, currentUser);
        }

        existingForm.PublishedVersion += 1;

        var published = FormDocument.Of(existingForm);
        var edit = copy is null || copy.Document.SameAs(published)
            ? null
            : copy.Append(published, [FormEditOp.ReplaceWith(published)], Guid.NewGuid(), userId, _time.GetUtcNow().UtcDateTime);

        if (edit is not null) _studio.Add(edit);

        await _uow.SaveChangesAsync(cancellationToken);

        await FormPublication.AfterCommitAsync(formId, publication, _draftService, _cache, cancellationToken);

        if (edit is not null) await _notifier.EditsAppliedAsync(formId, StudioEditContract.Of(edit, dirty: false), cancellationToken);
        await _notifier.PublishedAsync(new StudioPublishedContract(formId, existingForm.PublishedVersion, copy?.Version ?? 0, userId, published.ToJson(), false), cancellationToken);

        var collaboratorIds = existingForm.Collaborators.Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(
            ServiceStatus.Success,
            Data: FormEditorMapping.ContractOf(existingForm, users, role, FormEditorMapping.WorkflowRefOf(await FormEditorMapping.MembershipOfAsync(_workflows, formId, cancellationToken))));
    }

    public async Task<ServiceResult<List<FormCollaboratorContract>>> UpdateCollaboratorsAsync(Guid formId, FormCollaboratorsRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetForEditWithCollaboratorsAsync(formId, cancellationToken);
        if (form == null)
            return new ServiceResult<List<FormCollaboratorContract>>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var role = form.RoleOf(userId);
        if (!FormAccess.Allows(role, _currentUser.IsPlatformAdmin, FormAction.EditForm))
            return new ServiceResult<List<FormCollaboratorContract>>(ServiceStatus.NotAuthorized, Message: "Düzenleme ekibini değiştirme yetkiniz yok.");

        if (request.Collaborators is null || request.Collaborators.Any(collaborator => !Enum.IsDefined(collaborator.Role)))
            return new ServiceResult<List<FormCollaboratorContract>>(ServiceStatus.NotAcceptable, Message: "Bilinmeyen bir iş birliği rolü gönderildi.");

        form.UpdateCollaborators(request.Collaborators.Select(c => (c.UserId, c.Role)), (userId, role));

        await _uow.SaveChangesAsync(cancellationToken);

        var users = await _userService.GetUsersAsync(form.Collaborators.Select(c => c.UserId).ToList(), cancellationToken);
        var team = FormEditorMapping.ContractOf(form, users, role).Collaborators;

        await _notifier.TeamChangedAsync(new StudioTeamContract(formId, team), cancellationToken);

        return new ServiceResult<List<FormCollaboratorContract>>(ServiceStatus.Success, Data: team);
    }

    public async Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetForEditWithCollaboratorsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.DeleteForm))
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Formu yalnız sahibi silebilir.");

        if (await FormEditorMapping.MembershipOfAsync(_workflows, id, cancellationToken) is { IsPublished: true } membership)
        {
            return new ServiceResult<bool>(
                ServiceStatus.NotAcceptable,
                Message: $"Bu form '{membership.WorkflowName}' akışında kullanılıyor; önce akıştan çıkarın.");
        }

        form.Status = FormStatus.Deleted;

        await _uow.SaveChangesAsync(cancellationToken);

        await _draftService.ClearFormDraftsAsync(id, cancellationToken);
        await _draftService.ClearResponseDraftsAsync(id, cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Form silindi.");
    }

    public async Task<ServiceResult<bool>> RestoreFormAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetDeletedForEditAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Silinmiş form bulunamadı.");

        form.Status = FormStatus.Closed;

        await _uow.SaveChangesAsync(cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Form geri yüklendi.");
    }
}
