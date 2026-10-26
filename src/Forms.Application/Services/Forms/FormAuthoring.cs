using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Application.Validators;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Forms;

/// <summary>Formu yazanların komutları: oluşturma, güncelleme ve silme; yayınlanmış akıştaki formun kilitleri burada denetlenir.</summary>
public interface IFormAuthoring
{
    Task<ServiceResult<FormContract>> CreateFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormContract>> UpdateFormAsync(Guid formId, FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
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

    public FormAuthoring(
        IFormRepository forms,
        IFormsUnitOfWork uow,
        IUserDirectory userService,
        IFormDraftService draftService,
        ICurrentUser currentUser,
        ICacheService cache,
        IFormWorkflowRepository workflows,
        IGuestUploadService guestUploads)
    {
        _forms = forms;
        _uow = uow;
        _userService = userService;
        _draftService = draftService;
        _currentUser = currentUser;
        _cache = cache;
        _workflows = workflows;
        _guestUploads = guestUploads;
    }

    public async Task<ServiceResult<FormContract>> CreateFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var schema = contract.Schema ?? new();

        var validation = FormValidator.ValidateUpsert(contract, schema, _guestUploads.Capability is not null);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var task = NormalizeTask(contract.Task);
        var timeLimit = NormalizeTimeLimit(contract.TimeLimitMinutes);

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
            TimeLimitMinutes = timeLimit
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

        var schema = contract.Schema ?? new();

        var validation = FormValidator.ValidateUpsert(contract, schema, _guestUploads.Capability is not null);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var membership = await FormEditorMapping.MembershipOfAsync(_workflows, formId, cancellationToken);

        if (membership is { IsPublished: true })
        {
            var lockViolation = FindWorkflowLockViolation(existingForm, contract, membership);

            if (lockViolation is not null)
                return new ServiceResult<FormContract>(ServiceStatus.NotAcceptable, Message: lockViolation);
        }

        var task = NormalizeTask(contract.Task);
        var timeLimit = NormalizeTimeLimit(contract.TimeLimitMinutes);

        if (timeLimit.HasValue && membership is { IsPublished: true, AllowMultipleRuns: true })
        {
            return new ServiceResult<FormContract>(
                ServiceStatus.NotAcceptable,
                Message: $"Bu form '{membership.WorkflowName}' akışında kullanılıyor ve akış tekrar başlatılabiliyor; kişisel süre kullanılamaz.");
        }

        var timing = FormValidator.ValidateTiming(contract.AllowAnonymousResponses, membership is { IsPublished: true } ? false : contract.AllowMultipleResponses, task, timeLimit);
        if (timing.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(timing.Status, Message: timing.Message);

        var schemaChanged = !FormSchemaChange.IsSame(existingForm.Schema, schema);

        // Süreli formda taslak, süre dolunca kurulacak geçici cevabın tek kaynağıdır ve silinmez. Form bu kayıtta süreli
        // oluyorsa süre işlemeden yazılmış taslaklar gider; yoksa o geçici cevaba dönüşürlerdi.
        var clearResponseDrafts = !existingForm.HasTimeLimit
            && (timeLimit.HasValue || existingForm.Status != contract.Status || FormSchemaChange.BreaksDrafts(existingForm.Schema, schema));

        existingForm.Title = contract.Title;
        existingForm.Description = contract.Description;
        existingForm.Schema = schema;
        existingForm.Status = contract.Status;

        existingForm.AllowAnonymousResponses = contract.AllowAnonymousResponses;
        existingForm.AllowMultipleResponses = contract.AllowMultipleResponses;
        existingForm.RequiresManualReview = contract.RequiresManualReview;
        existingForm.Task = task;
        existingForm.ClosesAt = UtcDate.Normalize(contract.ClosesAt);
        existingForm.TimeLimitMinutes = timeLimit;

        if (contract.Collaborators != null)
        {
            var incomingCollaborators = contract.Collaborators.Select(c => (c.UserId, c.Role));
            var currentUser = (userId, role);

            existingForm.UpdateCollaborators(incomingCollaborators, currentUser);
        }

        await _uow.SaveChangesAsync(cancellationToken);

        await _draftService.ClearFormDraftsAsync(formId, cancellationToken);

        if (clearResponseDrafts)
            await _draftService.ClearResponseDraftsAsync(formId, cancellationToken);

        if (schemaChanged)
            await _cache.TryRemoveAsync(FormCacheKeys.Analytics(formId), cancellationToken);

        var collaboratorIds = existingForm.Collaborators.Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(
            ServiceStatus.Success,
            Data: FormEditorMapping.ContractOf(existingForm, users, role, FormEditorMapping.WorkflowRefOf(await FormEditorMapping.MembershipOfAsync(_workflows, formId, cancellationToken))));
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

    /// <summary>
    /// Yayınlanmış bir akışta kullanılan formda yalnız yönlendirmeyi bozan
    /// değişiklikler engellenir; soru ekleme, metin düzeltme ve sıralama serbesttir.
    /// </summary>
    private static string? FindWorkflowLockViolation(Form existingForm, FormUpsertRequest contract, WorkflowFormMembership workflowLock)
    {
        if (contract.Status != FormStatus.Open)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; kapatılamaz.";

        if (contract.AllowAnonymousResponses)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; anonim yanıtlara açılamaz.";

        // Yalnız sorunun şemada kalması denetlenir. Soru metnini değiştirmek serbesttir:
        // koşullar soruyu id ile okur. Seçenek adları istemci tarafında korunur, çünkü
        // backend şema içindeki seçenek listesini okumaz.
        var questionIds = (contract.Schema ?? new()).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var missing = workflowLock.LockedQuestions.FirstOrDefault(question => !questionIds.Contains(question.QuestionId));

        return missing is null
            ? null
            : $"'{missing.QuestionId}' sorusu '{workflowLock.WorkflowName}' akışının yönlendirme koşullarında kullanılıyor; formdan çıkarılamaz.";
    }

    private static FormTask? NormalizeTask(FormTask? task) =>
        task is null || string.IsNullOrWhiteSpace(task.Content)
            ? null
            : new FormTask { Content = task.Content, Collapsible = task.Collapsible, Downloadable = task.Downloadable };

    private static int? NormalizeTimeLimit(int? minutes) => minutes is > 0 ? minutes : null;
}
