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
using Skylab.Forms.Domain.Models;
using System.Text.Json;

namespace Skylab.Forms.Application.Services;

public class FormService : IFormService
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
    private readonly ICoreEventLookup _events;

    public FormService(
        IFormRepository forms,
        IFormResponseRepository responses,
        IFormsUnitOfWork uow,
        IExternalUserService userService,
        IFormDraftService draftService,
        ICurrentUserService currentUserService,
        ICacheService cache,
        IFormWorkflowRepository workflows,
        IFormWorkflowRuntime workflowRuntime,
        ICoreEventLookup events)
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
        _events = events;
    }

    public async Task<ServiceResult<FormContract>> CreateFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var schema = contract.Schema ?? new();
        var allowAnonymous = contract.AllowAnonymousResponses;
        var allowMultiple = contract.AllowMultipleResponses;
        if (contract.EventId.HasValue)
        {
            schema = EventIdentity.Ensure(schema);
            allowAnonymous = true;
            allowMultiple = true;
        }

        var validation = FormValidator.ValidateUpsert(allowAnonymous, allowMultiple, schema);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var formId = Guid.NewGuid();

        var newForm = new Form
        {
            Id = formId,
            Title = contract.Title,
            Description = contract.Description,
            Schema = schema,
            Status = contract.Status,
            AllowAnonymousResponses = allowAnonymous,
            AllowMultipleResponses = allowMultiple,
            RequiresManualReview = contract.RequiresManualReview,
            EventId = contract.EventId
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

        return new ServiceResult<FormContract>(ServiceStatus.Success, Data: await MapToContractAsync(newForm, users, CollaboratorRole.Owner, cancellationToken: cancellationToken));
    }

    public async Task<ServiceResult<FormContract>> UpdateFormAsync(Guid formId, FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var existingForm = await _forms.GetForEditWithDetailsAsync(formId, cancellationToken);
        if (existingForm == null)
            return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var currentUserCollaborator = existingForm.Collaborators.FirstOrDefault(c => c.UserId == userId);
        if (currentUserCollaborator == null || (currentUserCollaborator.Role != CollaboratorRole.Owner && currentUserCollaborator.Role != CollaboratorRole.Editor))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Bu formu düzenleme yetkiniz yok.");

        var schema = contract.Schema ?? new();
        var allowAnonymous = contract.AllowAnonymousResponses;
        var allowMultiple = contract.AllowMultipleResponses;
        var eventId = contract.EventId ?? existingForm.EventId;
        if (eventId.HasValue)
        {
            schema = EventIdentity.Ensure(schema);
            allowAnonymous = true;
            allowMultiple = true;
        }

        var validation = FormValidator.ValidateUpsert(allowAnonymous, allowMultiple, schema);
        if (validation.Status != ServiceStatus.Success)
            return new ServiceResult<FormContract>(validation.Status, Message: validation.Message);

        var membership = await FindMembershipAsync(formId, cancellationToken);

        if (membership is { IsPublished: true })
        {
            var lockViolation = FindWorkflowLockViolation(existingForm, contract, membership);

            if (lockViolation is not null)
                return new ServiceResult<FormContract>(ServiceStatus.NotAcceptable, Message: lockViolation);
        }

        bool statusChanged = existingForm.Status != contract.Status;

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string existingSchemaJson = JsonSerializer.Serialize(existingForm.Schema, jsonOptions);
        string newSchemaJson = JsonSerializer.Serialize(schema, jsonOptions);
        bool schemaChanged = existingSchemaJson != newSchemaJson;

        existingForm.Title = contract.Title;
        existingForm.Description = contract.Description;
        existingForm.Schema = schema;
        existingForm.Status = contract.Status;

        existingForm.AllowAnonymousResponses = allowAnonymous;
        existingForm.AllowMultipleResponses = allowMultiple;
        existingForm.RequiresManualReview = contract.RequiresManualReview;
        if (eventId.HasValue)
            existingForm.EventId = eventId;

        if (contract.Collaborators != null)
        {
            var incomingCollaborators = contract.Collaborators.Select(c => (c.UserId, c.Role));
            var currentUser = (userId, currentUserCollaborator.Role);

            existingForm.UpdateCollaborators(incomingCollaborators, currentUser);
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

        return new ServiceResult<FormContract>(
            ServiceStatus.Success,
            Data: await MapToContractAsync(existingForm, users, currentUserCollaborator.Role, ToWorkflowRef(await FindMembershipAsync(formId, cancellationToken)), cancellationToken));
    }

    public async Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithDetailsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var collaborator = form.Collaborators.FirstOrDefault(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor));
        if (collaborator == null && !await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var userRole = collaborator?.Role ?? CollaboratorRole.None;

        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(
            ServiceStatus.Success,
            Data: await MapToContractAsync(form, users, userRole, ToWorkflowRef(await FindMembershipAsync(id, cancellationToken)), cancellationToken));
    }

    public async Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted)
            return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotFound);

        // Kapalı form "yok" değildir: istemci bunu ayrı bir ekranla karşılayabilsin.
        if (form.Status == FormStatus.Closed)
            return new ServiceResult<FormDisplayPayload>(ServiceStatus.NotAvailable, Message: "Bu form şu anda yanıt kabul etmiyor.");

        if (userId == null && !form.AllowAnonymousResponses && form.EventId is null)
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

        var latestResponse = userId.HasValue
            ? await _responses.GetLatestForUserAsync(id, userId.Value, cancellationToken)
            : null;

        if (latestResponse is not null && !form.AllowMultipleResponses)
        {
            var answered = new FormDisplayPayload(null, 0, latestResponse.ReviewNote, latestResponse.ReviewedAt);

            return latestResponse.Status switch
            {
                FormResponseStatus.Pending => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.PendingApproval, answered, "Form cevabınız inceleniyor, lütfen bekleyiniz."),
                FormResponseStatus.Approved => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Approved, answered, "Başvurunuz onaylanmıştır."),
                FormResponseStatus.Declined => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Declined, answered, "Başvurunuz reddedilmiştir."),
                _ => new ServiceResult<FormDisplayPayload>(
                    ServiceStatus.Success, answered, "Bu formu daha önce doldurdunuz.")
            };
        }

        if (form.EventId is null)
        {
            var linked = await _events.FindByFormIdAsync(form.Id, cancellationToken);
            if (linked is not null) form.EventId = linked.Id;
        }

        return new ServiceResult<FormDisplayPayload>(ServiceStatus.Success, MapToDisplayPayload(form, 0));
    }

    public async Task<ServiceResult<FormMetaContract>> GetFormMetaByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetByIdAsync(id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted)
            return new ServiceResult<FormMetaContract>(ServiceStatus.NotFound);

        if (form.Status == FormStatus.Closed)
            return new ServiceResult<FormMetaContract>(ServiceStatus.NotAvailable, Message: "Bu form şu anda yanıt kabul etmiyor.");

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

        if (collaborator == null && !await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken))
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

        // Akış adımları listeden gizlenmez; hangi formun akışa ait olduğunu istemci
        // bu alandan görüp kendi süzmesini yapar.
        var memberships = await _workflows.GetFormMembershipsAsync(
            [.. data.Items.Select(form => form.Id)], cancellationToken);

        var items = data.Items
            .Select(form => form with
            {
                Workflow = memberships.TryGetValue(form.Id, out var membership) ? ToWorkflowRef(membership) : null
            })
            .ToList();

        items = await AttachEventsAsync(items, cancellationToken);

        return new ServiceResult<PagedResult<FormSummaryContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<FormSummaryContract>(items, data.TotalCount, data.Page, data.PageSize));
    }

    public async Task<ServiceResult<PagedResult<FormAllSummaryContract>>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken cancellationToken = default)
    {
        var raw = await _forms.GetAllFormsAsync(request, cancellationToken);

        var ownerIds = raw.Items.Select(f => f.OwnerUserId).Distinct().ToList();
        var users = await _userService.GetUsersAsync(ownerIds, cancellationToken);
        var userMap = users.ToDictionary(u => u.Id);

        var memberships = await _workflows.GetFormMembershipsAsync(
            [.. raw.Items.Select(form => form.Id)], cancellationToken);

        var forms = raw.Items.Select(f =>
        {
            userMap.TryGetValue(f.OwnerUserId, out var owner);
            return new FormAllSummaryContract(
                f.Id,
                f.Title,
                f.Status,
                owner ?? new UserContract(f.OwnerUserId, null, null, null),
                f.AllowAnonymousResponses,
                f.AllowMultipleResponses,
                f.RequiresManualReview,
                memberships.TryGetValue(f.Id, out var membership) ? ToWorkflowRef(membership) : null,
                f.CreatedAt,
                f.UpdatedAt,
                f.ResponseCount
            );
        }).ToList();

        var byForm = (await _events.FindByFormIdsAsync(forms.Select(f => f.Id), cancellationToken))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var row in raw.Items)
        {
            if (byForm.ContainsKey(row.Id) || row.EventId is not Guid eventId) continue;
            var ev = await _events.FindByIdAsync(eventId, cancellationToken);
            if (ev is not null) byForm[row.Id] = ev;
        }
        forms = forms
            .Select(f => f with { Event = byForm.TryGetValue(f.Id, out var ev) ? ev : f.Event })
            .ToList();

        return new ServiceResult<PagedResult<FormAllSummaryContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<FormAllSummaryContract>(forms, raw.TotalCount, raw.Page, raw.PageSize)
        );
    }

    public async Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetForEditOwnedByAsync(id, userId, cancellationToken);

        if (form == null) return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Form bulunamadı veya yetkiniz yok.");

        if (await FindMembershipAsync(id, cancellationToken) is { IsPublished: true } membership)
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

    private async Task<List<FormSummaryContract>> AttachEventsAsync(
        List<FormSummaryContract> items,
        CancellationToken cancellationToken)
    {
        var byForm = (await _events.FindByFormIdsAsync(items.Select(form => form.Id), cancellationToken))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var form in items)
        {
            if (byForm.ContainsKey(form.Id) || form.EventId is not Guid eventId) continue;
            var ev = await _events.FindByIdAsync(eventId, cancellationToken);
            if (ev is not null) byForm[form.Id] = ev;
        }
        return items
            .Select(form => form with { Event = byForm.TryGetValue(form.Id, out var ev) ? ev : form.Event })
            .ToList();
    }

    private async Task<FormContract> MapToContractAsync(
        Form form,
        List<UserContract> users,
        CollaboratorRole userRole = CollaboratorRole.None,
        FormWorkflowRefContract? workflow = null,
        CancellationToken cancellationToken = default)
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

        var ev = form.EventId is Guid eventId
            ? await _events.FindByIdAsync(eventId, cancellationToken)
            : null;
        ev ??= await _events.FindByFormIdAsync(form.Id, cancellationToken);

        return new FormContract(
            form.Id,
            form.Title,
            form.Description,
            DisplaySchema(form),
            form.Status,
            form.AllowAnonymousResponses,
            form.AllowMultipleResponses,
            form.RequiresManualReview,
            workflow,
            userRole,
            collaboratorContracts,
            form.CreatedAt,
            form.UpdatedAt,
            ev
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

            contract = new FormDisplayContract(target.Id, target.Title, target.Description, DisplaySchema(target), target.EventId);
        }

        var payload = new FormDisplayPayload(
            contract,
            LegacyStep.From(outcome),
            outcome.ReviewNote,
            outcome.ReviewedAt,
            outcome.InstanceId,
            outcome.State,
            outcome.Stage,
            outcome.StartFormId);

        return new ServiceResult<FormDisplayPayload>(workflow.Status, payload, workflow.Message);
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

    private async Task<WorkflowFormMembership?> FindMembershipAsync(Guid formId, CancellationToken cancellationToken)
    {
        var memberships = await _workflows.GetFormMembershipsAsync([formId], cancellationToken);

        return memberships.TryGetValue(formId, out var membership) ? membership : null;
    }

    private static FormWorkflowRefContract? ToWorkflowRef(WorkflowFormMembership? membership) =>
        membership is null
            ? null
            : new FormWorkflowRefContract(
                membership.WorkflowId,
                membership.WorkflowName,
                membership.IsStart,
                membership.IsPublished,
                membership.AllowMultipleRuns,
                membership.RequiresManualReview,
                [.. membership.LockedQuestions.Select(question => new FormLockedQuestionContract(question.QuestionId, [.. question.Values]))]);

    private static List<FormSchemaItem> DisplaySchema(Form form) =>
        form.EventId.HasValue ? EventIdentity.Ensure(form.Schema) : form.Schema;

    private FormDisplayPayload MapToDisplayPayload(Form form, int step, string? reviewNote = null, DateTime? reviewedAt = null)
    {
        var contract = new FormDisplayContract(
            form.Id,
            form.Title,
            form.Description,
            DisplaySchema(form),
            form.EventId
        );

        return new FormDisplayPayload(contract, step, reviewNote, reviewedAt);
    }
}
