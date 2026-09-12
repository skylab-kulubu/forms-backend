using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Application.Validators;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using System.Text.Json;

namespace Skylab.Forms.Application.Services;

public partial class FormService : IFormService
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IFormsUnitOfWork _uow;
    private readonly IExternalUserService _userService;
    private readonly IFormDraftService _draftService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ICacheService _cache;
    private readonly IFormWorkflowRepository _workflows;
    private readonly IFormWorkflowRuntime _workflowRuntime;

    public FormService(
        IFormRepository forms,
        IFormResponseRepository responses,
        IFormsUnitOfWork uow,
        IExternalUserService userService,
        IFormDraftService draftService,
        ICurrentUserService currentUserService,
        ICacheService cache,
        IFormWorkflowRepository workflows,
        IFormWorkflowRuntime workflowRuntime)
    {
        _forms = forms;
        _responses = responses;
        _uow = uow;
        _userService = userService;
        _draftService = draftService;
        _currentUserService = currentUserService;
        _cache = cache;
        _workflows = workflows;
        _workflowRuntime = workflowRuntime;
    }

    public async Task<ServiceResult<FormContract>> CreateFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var validation = FormValidator.ValidateUpsert(contract.AllowAnonymousResponses, contract.AllowMultipleResponses, contract.Schema, contract.LinkedFormId);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var formId = Guid.NewGuid();

        var newForm = new Form
        {
            Id = formId,
            Title = contract.Title,
            Description = contract.Description,
            Schema = contract.Schema ?? new(),
            Status = contract.Status,
            AllowAnonymousResponses = contract.AllowAnonymousResponses,
            AllowMultipleResponses = contract.AllowMultipleResponses,
            RequiresManualReview = contract.RequiresManualReview,
            LinkedFormId = null
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

        return new ServiceResult<FormContract>(ServiceStatus.Success, Data: MapToContract(newForm, users, isChildForm: false, userRole: CollaboratorRole.Owner, null));
    }

    public async Task<ServiceResult<FormContract>> UpdateFormAsync(Guid formId, FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var existingForm = await _forms.GetForEditWithDetailsAsync(formId, cancellationToken);
        if (existingForm == null)
            return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var currentUserCollaborator = existingForm.Collaborators.FirstOrDefault(c => c.UserId == userId);
        if (currentUserCollaborator == null || (currentUserCollaborator.Role != CollaboratorRole.Owner && currentUserCollaborator.Role != CollaboratorRole.Editor))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Bu formu düzenleme yetkiniz yok.");

        var validation = FormValidator.ValidateUpsert(contract.AllowAnonymousResponses, contract.AllowMultipleResponses, contract.Schema, contract.LinkedFormId);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var workflowLock = await _workflows.GetPublishedLockAsync(formId, cancellationToken);

        if (workflowLock is not null)
        {
            var lockViolation = FindWorkflowLockViolation(existingForm, contract, workflowLock);

            if (lockViolation is not null)
                return new ServiceResult<FormContract>(ServiceStatus.NotAcceptable, Message: lockViolation);
        }

        bool statusChanged = existingForm.Status != contract.Status;

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string existingSchemaJson = JsonSerializer.Serialize(existingForm.Schema, jsonOptions);
        string newSchemaJson = JsonSerializer.Serialize(contract.Schema ?? new(), jsonOptions);
        bool schemaChanged = existingSchemaJson != newSchemaJson;

        var isChildForm = await _forms.IsChildFormAsync(formId, cancellationToken);

        existingForm.Title = contract.Title;
        existingForm.Description = contract.Description;
        existingForm.Schema = contract.Schema ?? new();
        existingForm.Status = contract.Status;

        if (existingForm.LinkedFormId != contract.LinkedFormId)
        {
            if (!contract.LinkedFormId.HasValue && existingForm.LinkedFormId.HasValue)
            {
                var unlinkResult = await ApplyUnlinkInternalAsync(existingForm, userId, cancellationToken);
                if (unlinkResult.Status != ServiceStatus.Success) return new ServiceResult<FormContract>(unlinkResult.Status, Message: unlinkResult.Message);
            }
            else if (contract.LinkedFormId.HasValue)
            {
                var linkResult = await ApplyLinkInternalAsync(existingForm, contract.LinkedFormId.Value, userId, cancellationToken);
                if (linkResult.Status != ServiceStatus.Success) return new ServiceResult<FormContract>(linkResult.Status, Message: linkResult.Message);
            }
        }

        if (!isChildForm)
        {
            existingForm.AllowAnonymousResponses = contract.AllowAnonymousResponses;
            existingForm.AllowMultipleResponses = contract.AllowMultipleResponses;
            existingForm.RequiresManualReview = contract.RequiresManualReview;

            if (contract.Collaborators != null)
            {
                var incomingCollaborators = contract.Collaborators.Select(c => (c.UserId, c.Role));
                var currentUser = (userId, currentUserCollaborator.Role);

                existingForm.UpdateCollaborators(incomingCollaborators, currentUser);
            }

            if (existingForm.LinkedFormId.HasValue)
            {
                var childForm = await _forms.GetForEditWithCollaboratorsAsync(existingForm.LinkedFormId.Value, cancellationToken);
                if (childForm != null)
                {
                    childForm.Status = existingForm.Status;
                    childForm.AllowAnonymousResponses = existingForm.AllowAnonymousResponses;
                    childForm.AllowMultipleResponses = existingForm.AllowMultipleResponses;
                    childForm.RequiresManualReview = existingForm.RequiresManualReview;

                    childForm.SyncChildCollaborators(existingForm.Collaborators);
                }
            }
        }

        await _uow.SaveChangesAsync(cancellationToken);

        await _draftService.ClearFormDraftsAsync(formId, cancellationToken);

        if (statusChanged || schemaChanged)
        {
            await _draftService.ClearResponseDraftsAsync(formId, cancellationToken);
        }

        if (schemaChanged)
            await _cache.TryRemoveAsync(FormCacheKeys.Analytics(formId), cancellationToken);

        var collaboratorIds = existingForm.Collaborators.Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(ServiceStatus.Success, Data: MapToContract(existingForm, users, isChildForm, currentUserCollaborator.Role, existingForm.LinkedForm?.Title));
    }

    public async Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithDetailsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var collaborator = form.Collaborators.FirstOrDefault(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor));
        if (collaborator == null && !await _currentUserService.HasRoleAsync("skyforms:*", "dotnet", cancellationToken))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var userRole = collaborator?.Role ?? CollaboratorRole.None;

        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        var isChildForm = await _forms.IsChildFormAsync(id, cancellationToken);
        return new ServiceResult<FormContract>(ServiceStatus.Success, Data: MapToContract(form, users, isChildForm, userRole, form.LinkedForm?.Title));
    }

    public async Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted || form.Status == FormStatus.Closed) return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotFound);

        if (userId == null && (!form.AllowAnonymousResponses || form.LinkedFormId.HasValue))
        {
            return new ServiceResult<FormDisplayPayload>(
                ServiceStatus.Unauthorized,
                Message: "Bu formu görüntülemek için giriş yapmalısınız."
            );
        }

        if (userId.HasValue)
        {
            var workflow = await _workflowRuntime.ResolveDisplayAsync(id, userId.Value, cancellationToken);

            // Veri yoksa hata vardır; ikisi de akış yoluna aittir.
            if (workflow.Data is not { State: WorkflowActionState.NotInWorkflow })
                return await MapWorkflowDisplayAsync(workflow, cancellationToken);
        }

        var parentForm = await _forms.GetParentOfAsync(id, cancellationToken);

        bool isParent = form.LinkedFormId.HasValue;
        bool isChild = parentForm != null;

        FormResponse? parentResponse = null;

        if (parentForm != null)
        {
            if (userId == null)
            {
                return new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Unauthorized,
                    new FormDisplayPayload(null, 1, null, null),
                    "Bağlı form akışı için giriş yapmalısınız."
                );
            }

            parentResponse = await _responses.GetLatestForUserAsync(parentForm.Id, userId.Value, cancellationToken);
            if (parentForm.RequiresManualReview)
            {
                if (parentResponse == null || parentResponse.Status != FormResponseStatus.Approved)
                {
                    return new ServiceResult<FormDisplayPayload>(
                        ServiceStatus.RequiresParentApproval,
                        new FormDisplayPayload(null, 2, null, null),
                        "Bu formu görüntülemek için önceki adımın onaylanması gerekmektedir."
                    );
                }
            }
            else
            {
                if (parentResponse == null) return await GetDisplayFormByIdAsync(parentForm.Id, userId, cancellationToken);
            }
        }

        bool isLinkedFlow = isParent || isChild;
        var latestResponse = userId.HasValue
            ? await _responses.GetLatestForUserAsync(id, userId.Value, cancellationToken)
            : null;

        int step = isLinkedFlow ? ResolveStep(isChild, latestResponse?.Status) : 0;

        if (latestResponse != null)
        {
            var note = latestResponse.ReviewNote;
            var date = latestResponse.ReviewedAt;

            if (latestResponse.Status == FormResponseStatus.Pending)
            {
                return new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.PendingApproval,
                    new FormDisplayPayload(null, step, null, null),
                    "Form cevabınız inceleniyor, lütfen bekleyiniz."
                );
            }
            if (latestResponse.Status == FormResponseStatus.Approved || latestResponse.Status == FormResponseStatus.NonRestrict)
            {
                if (form.LinkedFormId.HasValue)
                {
                    if (form.AllowMultipleResponses)
                    {
                        var childResponse = userId.HasValue
                            ? await _responses.GetLatestForUserAsync(form.LinkedFormId.Value, userId.Value, cancellationToken)
                            : null;

                        bool childCompleted = childResponse != null &&
                            (childResponse.Status == FormResponseStatus.Approved || childResponse.Status == FormResponseStatus.NonRestrict) &&
                            childResponse.SubmittedAt > latestResponse.SubmittedAt;

                        if (!childCompleted)
                            return await GetDisplayFormByIdAsync(form.LinkedFormId.Value, userId, cancellationToken);

                        return new ServiceResult<FormDisplayPayload>(
                            ServiceStatus.Success,
                            MapToDisplayPayload(form, 1, null, null)
                        );
                    }

                    return await GetDisplayFormByIdAsync(form.LinkedFormId.Value, userId, cancellationToken);
                }

                if (isChild)
                {
                    if (form.AllowMultipleResponses && parentResponse != null && parentResponse.SubmittedAt > latestResponse.SubmittedAt)
                    {
                        return new ServiceResult<FormDisplayPayload>(
                            ServiceStatus.Success,
                            MapToDisplayPayload(form, 3, null, null)
                        );
                    }

                    return new ServiceResult<FormDisplayPayload>(
                        ServiceStatus.Completed,
                        new FormDisplayPayload(null, step, note, date),
                        "Tüm adımları tamamladınız."
                    );
                }

                if (!form.AllowMultipleResponses)
                {
                    return new ServiceResult<FormDisplayPayload>(
                        latestResponse.Status == FormResponseStatus.Approved ? ServiceStatus.Approved : ServiceStatus.Success,
                        new FormDisplayPayload(null, step, note, date),
                        latestResponse.Status == FormResponseStatus.Approved ? "Başvurunuz onaylanmıştır." : "Bu formu daha önce doldurdunuz."
                    );
                }
            }
            if (latestResponse.Status == FormResponseStatus.Declined)
            {
                if (!form.AllowMultipleResponses)
                {
                    return new ServiceResult<FormDisplayPayload>(
                        ServiceStatus.Declined,
                        new FormDisplayPayload(null, step, note, date),
                        "Başvurunuz reddedilmiştir."
                    );
                }
            }
        }

        return new ServiceResult<FormDisplayPayload>(
            ServiceStatus.Success,
            MapToDisplayPayload(form, step, null, null)
        );
    }

    public async Task<ServiceResult<FormMetaContract>> GetFormMetaByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted || form.Status == FormStatus.Closed)
            return new ServiceResult<FormMetaContract>(ServiceStatus.NotFound);

        return new ServiceResult<FormMetaContract>(
            ServiceStatus.Success,
            Data: new FormMetaContract(form.Title, form.Description)
        );
    }

    public async Task<ServiceResult<FormInfoContract>> GetFormInfoByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<FormInfoContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var collaborator = form.Collaborators.FirstOrDefault(c => c.UserId == userId && c.Role != CollaboratorRole.None);

        if (collaborator == null && !await _currentUserService.HasRoleAsync("skyforms:*", "dotnet", cancellationToken))
            return new ServiceResult<FormInfoContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var counts = await _responses.GetCountsAsync(id, cancellationToken);

        var contract = new FormInfoContract(
            form.Id,
            form.Title,
            form.Status,
            form.UpdatedAt ?? form.CreatedAt,
            counts.Total,
            counts.Waiting,
            AverageTimeSeconds: counts.AverageTimeSpentSeconds,
            LastSeenUsers: Array.Empty<FormLastSeenUserContract>(),
            UserRole: collaborator?.Role ?? CollaboratorRole.None
        );

        return new ServiceResult<FormInfoContract>(ServiceStatus.Success, Data: contract);
    }

    public async Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default)
    {
        var data = await _forms.GetUserFormsAsync(userId, request, cancellationToken);
        return new ServiceResult<PagedResult<FormSummaryContract>>(ServiceStatus.Success, Data: data);
    }

    public async Task<ServiceResult<PagedResult<FormAllSummaryContract>>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken cancellationToken = default)
    {
        var raw = await _forms.GetAllFormsAsync(request, cancellationToken);

        var ownerIds = raw.Items.Select(f => f.OwnerUserId).Distinct().ToList();
        var users = await _userService.GetUsersAsync(ownerIds, cancellationToken);
        var userMap = users.ToDictionary(u => u.Id);

        var forms = raw.Items.Select(f =>
        {
            userMap.TryGetValue(f.OwnerUserId, out var owner);
            return new FormAllSummaryContract(
                f.Id,
                f.Title,
                f.Status,
                f.LinkedForm,
                owner ?? new UserContract(f.OwnerUserId, null, null, null),
                f.AllowAnonymousResponses,
                f.AllowMultipleResponses,
                f.RequiresManualReview,
                f.CreatedAt,
                f.UpdatedAt,
                f.ResponseCount
            );
        }).ToList();

        return new ServiceResult<PagedResult<FormAllSummaryContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<FormAllSummaryContract>(forms, raw.TotalCount, raw.Page, raw.PageSize)
        );
    }

    public async Task<ServiceResult<List<LinkableFormsContract>>> GetLinkableFormsAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var currentForm = await _forms.GetByIdAsync(id, cancellationToken);

        if (currentForm == null)
            return new ServiceResult<List<LinkableFormsContract>>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var forms = await _forms.GetLinkableFormsAsync(id, userId, cancellationToken);
        return new ServiceResult<List<LinkableFormsContract>>(ServiceStatus.Success, Data: forms);
    }

    public async Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetForEditOwnedByAsync(id, userId, cancellationToken);

        if (form == null) return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Form bulunamadı veya yetkiniz yok.");

        if (await _workflows.GetPublishedLockAsync(id, cancellationToken) is { } workflowLock)
        {
            return new ServiceResult<bool>(
                ServiceStatus.NotAcceptable,
                Message: $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; önce akıştan çıkarın.");
        }

        var parentForm = await _forms.GetParentOfForEditAsync(id, cancellationToken);

        if (parentForm != null)
        {
            parentForm.LinkedFormId = null;
            parentForm.UpdatedAt = DateTime.UtcNow;
        }

        form.LinkedFormId = null;
        form.Status = FormStatus.Deleted;

        await _uow.SaveChangesAsync(cancellationToken);

        await _draftService.ClearFormDraftsAsync(id, cancellationToken);
        await _draftService.ClearResponseDraftsAsync(id, cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Form silindi.");
    }

    private static FormContract MapToContract(Form form, List<UserContract> users, bool isChildForm = false, CollaboratorRole userRole = CollaboratorRole.None, string? linkedFormTitle = null)
    {
        var collaboratorContracts = new List<FormCollaboratorContract>();

        if (form.Collaborators != null)
        {
            foreach (var collaborator in form.Collaborators)
            {
                var userDetail = users.FirstOrDefault(u => u.Id == collaborator.UserId) ?? new UserContract(collaborator.UserId, null, null, null);
                collaboratorContracts.Add(new FormCollaboratorContract(
                    userDetail,
                    collaborator.Role
                ));
            }
        }

        LinkedFormContract? linkedFormSummary = null;
        if (form.LinkedFormId.HasValue && !string.IsNullOrEmpty(linkedFormTitle))
        {
            linkedFormSummary = new LinkedFormContract(form.LinkedFormId.Value, linkedFormTitle);
        }

        return new FormContract(
            form.Id,
            form.Title,
            form.Description,
            form.Schema,
            form.Status,
            form.AllowAnonymousResponses,
            form.AllowMultipleResponses,
            form.RequiresManualReview,
            linkedFormSummary,
            isChildForm,
            userRole,
            collaboratorContracts,
            form.CreatedAt,
            form.UpdatedAt
        );
    }

    private async Task<ServiceResult<FormDisplayPayload>> MapWorkflowDisplayAsync(
        ServiceResult<WorkflowStepOutcome> workflow,
        CancellationToken cancellationToken)
    {
        if (workflow.Data is not { } outcome)
            return new ServiceResult<FormDisplayPayload>(workflow.Status, Message: workflow.Message);

        FormDisplayContract? contract = null;

        if (outcome is { State: WorkflowActionState.ShowForm, FormId: { } targetFormId })
        {
            var target = await _forms.GetByIdAsync(targetFormId, cancellationToken);
            if (target == null) return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotFound);

            contract = new FormDisplayContract(target.Id, target.Title, target.Description, target.Schema);
        }

        var payload = new FormDisplayPayload(
            contract,
            LegacyStep.From(outcome),
            outcome.ReviewNote,
            outcome.ReviewedAt,
            outcome.InstanceId,
            outcome.State,
            outcome.Stage);

        return new ServiceResult<FormDisplayPayload>(workflow.Status, payload, workflow.Message);
    }

    /// <summary>
    /// Yayınlanmış bir akışta kullanılan formda yalnız yönlendirmeyi bozan
    /// değişiklikler engellenir; soru ekleme, metin düzeltme ve sıralama serbesttir.
    /// </summary>
    private static string? FindWorkflowLockViolation(Form existingForm, FormUpsertRequest contract, WorkflowFormLock workflowLock)
    {
        if (contract.Status != FormStatus.Open)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; kapatılamaz.";

        if (contract.RequiresManualReview != existingForm.RequiresManualReview)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; onay ayarı değiştirilemez.";

        if (contract.AllowAnonymousResponses)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; anonim yanıtlara açılamaz.";

        var questionIds = (contract.Schema ?? new()).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var missing = workflowLock.LockedQuestionIds.FirstOrDefault(questionId => !questionIds.Contains(questionId));

        return missing is null
            ? null
            : $"'{missing}' sorusu '{workflowLock.WorkflowName}' akışının yönlendirme koşullarında kullanılıyor; silinemez veya yeniden adlandırılamaz.";
    }

    private FormDisplayPayload MapToDisplayPayload(Form form, int step, string? reviewNote = null, DateTime? reviewedAt = null)
    {
        var contract = new FormDisplayContract(
            form.Id,
            form.Title,
            form.Description,
            form.Schema
        );

        return new FormDisplayPayload(contract, step, reviewNote, reviewedAt);
    }

    private static int ResolveStep(bool isChild, FormResponseStatus? responseStatus)
    {
        if (!isChild) // parent form
        {
            return responseStatus switch
            {
                null or FormResponseStatus.Declined => 1,
                FormResponseStatus.Pending => 2,
                _ => 3
            };
        }
        else // child form
        {
            return responseStatus switch
            {
                null or FormResponseStatus.Declined => 3,
                FormResponseStatus.Pending => 4,
                _ => 5
            };
        }
    }
}
