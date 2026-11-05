using System.Text.Json;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Application.Services.Studio;
using Skylab.Forms.Application.Validators;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public class FormWorkflowService : IFormWorkflowService
{
    private readonly IFormWorkflowRepository _workflows;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly IFormsUnitOfWork _uow;
    private readonly IUserDirectory _userService;
    private readonly ICurrentUser _currentUser;
    private readonly IFormWorkflowRuntime _runtime;
    private readonly IFormMailNotifier _mailNotifier;
    private readonly IFormWorkflowRunService _runs;
    private readonly IFormPublishing _publishing;
    private readonly IGuestUploadService _guestUploads;
    private readonly TimeProvider _time;

    public FormWorkflowService(
        IFormWorkflowRepository workflows,
        IFormWorkflowInstanceRepository instances,
        IFormsUnitOfWork uow,
        IUserDirectory userService,
        ICurrentUser currentUser,
        IFormWorkflowRuntime runtime,
        IFormMailNotifier mailNotifier,
        IFormWorkflowRunService runs,
        IFormPublishing publishing,
        IGuestUploadService guestUploads,
        TimeProvider time)
    {
        _workflows = workflows;
        _instances = instances;
        _uow = uow;
        _userService = userService;
        _currentUser = currentUser;
        _runtime = runtime;
        _mailNotifier = mailNotifier;
        _runs = runs;
        _publishing = publishing;
        _guestUploads = guestUploads;
        _time = time;
    }

    public async Task<ServiceResult<WorkflowContract>> CreateAsync(
        WorkflowUpsertRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (NameError(request.Name) is { } error)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: error);

        var workflow = new FormWorkflow
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            OwnerUserId = userId,
            AllowMultipleRuns = request.AllowMultipleRuns,
            Status = WorkflowStatus.Draft
        };

        _workflows.Add(workflow);
        _workflows.Add(new FormWorkflowVersion { WorkflowId = workflow.Id, Version = 1, Status = WorkflowStatus.Draft });

        await _uow.SaveChangesAsync(cancellationToken);

        return await BuildContractAsync(workflow, userId, cancellationToken);
    }

    public async Task<ServiceResult<WorkflowContract>> UpdateAsync(
        Guid workflowId,
        WorkflowUpsertRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowContract>();

        if (NameError(request.Name) is { } error)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: error);

        if (request.AllowMultipleRuns && !workflow.AllowMultipleRuns && await HasTimedStepAsync(workflowId, cancellationToken))
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Akışta kişisel süreli bir form var; tekrar başlatma açılamaz.");

        workflow.Name = request.Name.Trim();
        workflow.Description = request.Description;
        workflow.AllowMultipleRuns = request.AllowMultipleRuns;

        await _uow.SaveChangesAsync(cancellationToken);

        return await BuildContractAsync(workflow, userId, cancellationToken);
    }

    public async Task<ServiceResult<WorkflowContract>> UpdateIntakeScheduleAsync(
        Guid workflowId,
        WorkflowIntakeScheduleRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowContract>();

        if (workflow.Status == WorkflowStatus.Archived)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş akış düzenlenemez.");

        workflow.IntakeClosesAt = UtcDate.Normalize(request.ClosesAt);

        await _uow.SaveChangesAsync(cancellationToken);

        return await BuildContractAsync(workflow, userId, cancellationToken);
    }

    private async Task<bool> HasTimedStepAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var draft = await _workflows.GetVersionAsync(workflowId, WorkflowStatus.Draft, cancellationToken);
        var published = await _workflows.GetVersionAsync(workflowId, WorkflowStatus.Published, cancellationToken);

        var formIds = (draft?.Nodes ?? []).Concat(published?.Nodes ?? [])
            .Select(node => node.FormId)
            .Distinct()
            .ToList();

        if (formIds.Count == 0) return false;

        var headers = await _workflows.GetFormHeadersAsync(formIds, cancellationToken);

        return headers.Values.Any(header => header.TimeLimitMinutes is > 0);
    }

    public async Task<ServiceResult<WorkflowContract>> UpdateIntakeAsync(
        Guid workflowId,
        WorkflowIntakeRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowContract>();

        if (workflow.Status == WorkflowStatus.Archived)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş akış yeniden açılamaz.");

        if (request.Intake is not { } intake || !Enum.IsDefined(intake))
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Geçersiz başvuru durumu.");

        workflow.Intake = intake;

        await _uow.SaveChangesAsync(cancellationToken);

        return await BuildContractAsync(workflow, userId, cancellationToken);
    }

    public async Task<ServiceResult<WorkflowContract>> GetAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowContract>();

        if (workflow.OwnerUserId != userId && !_currentUser.IsPlatformAdmin)
            return NotOwner<WorkflowContract>();

        return await BuildContractAsync(workflow, userId, cancellationToken);
    }

    public async Task<ServiceResult<PagedResult<WorkflowSummaryContract>>> GetOwnedAsync(
        Guid userId,
        GetWorkflowsRequest request,
        CancellationToken cancellationToken = default)
    {
        var page = await _workflows.GetOwnedWorkflowsAsync(userId, request, cancellationToken);
        var workflows = page.Items;

        var startFormIds = workflows
            .Where(workflow => workflow.StartFormId.HasValue)
            .Select(workflow => workflow.StartFormId!.Value)
            .Distinct()
            .ToList();

        var headers = await _workflows.GetFormHeadersAsync(startFormIds, cancellationToken);
        var runs = await _runs.GetOverviewsAsync([.. workflows.Select(workflow => workflow.Id)], cancellationToken);

        var summaries = workflows
            .Select(workflow => new WorkflowSummaryContract(
                workflow.Id,
                workflow.Name,
                workflow.Status,
                workflow.AllowMultipleRuns,
                workflow.Intake,
                ToFormRef(workflow.StartFormId, headers),
                workflow.NodeCount,
                workflow.PublishedVersion,
                workflow.HasUnpublishedChanges,
                workflow.UpdatedAt,
                runs.GetValueOrDefault(workflow.Id)))
            .ToList();

        return new ServiceResult<PagedResult<WorkflowSummaryContract>>(
            ServiceStatus.Success,
            new PagedResult<WorkflowSummaryContract>(summaries, page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<ServiceResult<List<WorkflowVersionSummaryContract>>> GetVersionsAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<List<WorkflowVersionSummaryContract>>();

        if (workflow.OwnerUserId != userId && !_currentUser.IsPlatformAdmin)
            return NotOwner<List<WorkflowVersionSummaryContract>>();

        var versions = await _workflows.GetVersionsAsync(workflowId, cancellationToken);

        var summaries = versions
            .Select(version => new WorkflowVersionSummaryContract(
                version.Id,
                version.Version,
                version.Status,
                version.PublishedAt,
                version.NodeCount))
            .ToList();

        return new ServiceResult<List<WorkflowVersionSummaryContract>>(ServiceStatus.Success, summaries);
    }

    public async Task<ServiceResult<WorkflowContract>> UpdateDefinitionAsync(
        Guid workflowId,
        WorkflowDefinitionRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowContract>();

        if (workflow.Status == WorkflowStatus.Archived)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş akış düzenlenemez.");

        var draft = await PrepareDraftAsync(workflowId, request, cancellationToken);
        if (draft.Error is not null)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: draft.Error);

        var published = await _workflows.GetVersionAsync(workflowId, WorkflowStatus.Published, cancellationToken);

        if (published is not null && HasSameGraph(draft.Nodes, draft.Transitions, published))
        {
            if (!draft.IsNew)
            {
                RemoveDraft(draft.Version);

                await _uow.SaveChangesAsync(cancellationToken);
            }

            return await BuildContractAsync(workflow, userId, cancellationToken);
        }

        if (draft.IsNew) _workflows.Add(draft.Version);

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            await ReplaceDraftGraphAsync(draft, token);

            return true;
        }, cancellationToken);

        return await BuildContractAsync(workflow, userId, cancellationToken);
    }

    private async Task<DraftGraph> PrepareDraftAsync(
        Guid workflowId,
        WorkflowDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var draft = await _workflows.GetVersionForEditAsync(workflowId, WorkflowStatus.Draft, cancellationToken);
        var isNewDraft = draft is null;

        if (draft is null)
        {
            // Yayınlanmış version değişmez; düzenleme yeni bir taslak üretir.
            draft = new FormWorkflowVersion
            {
                WorkflowId = workflowId,
                Version = await _workflows.GetNextVersionNumberAsync(workflowId, cancellationToken),
                Status = WorkflowStatus.Draft
            };
        }

        var unsetReviewFormIds = request.Nodes
            .Where(node => node.RequiresManualReview is null)
            .Select(node => node.FormId)
            .Distinct()
            .ToList();

        var defaults = unsetReviewFormIds.Count == 0
            ? new Dictionary<Guid, WorkflowFormHeader>()
            : await _workflows.GetFormHeadersAsync(unsetReviewFormIds, cancellationToken);

        var build = BuildGraph(draft.Id, request, defaults);

        return new DraftGraph(draft, isNewDraft, build.Nodes, build.Transitions, build.Error);
    }

    private sealed record DraftGraph(
        FormWorkflowVersion Version,
        bool IsNew,
        List<FormWorkflowNode> Nodes,
        List<FormWorkflowTransition> Transitions,
        string? Error);

    // Silme ve ekleme tek kayıt işleminde gönderilirse EF ekleme komutlarını
    // önce yazabilir ve node anahtarı üzerindeki tekil indeks ihlal edilir.
    private async Task ReplaceDraftGraphAsync(DraftGraph draft, CancellationToken token)
    {
        _workflows.RemoveRange(draft.Version.Transitions.ToList());
        _workflows.RemoveRange(draft.Version.Nodes.ToList());

        await _uow.SaveChangesAsync(token);

        _workflows.AddRange(draft.Nodes);
        _workflows.AddRange(draft.Transitions);

        await _uow.SaveChangesAsync(token);
    }

    private void RemoveDraft(FormWorkflowVersion draft)
    {
        _workflows.RemoveRange(draft.Transitions.ToList());
        _workflows.RemoveRange(draft.Nodes.ToList());
        _workflows.Remove(draft);
    }

    // Eskiyi arşivlemek ile yeniyi yayınlamak tek kayıt işleminde gönderilirse
    // iki yayınlanmış version aynı anda var olur ve tekil indeks ihlal edilir.
    private async Task PromoteAsync(FormWorkflow workflow, FormWorkflowVersion draft, FormWorkflowVersion? published, CancellationToken token)
    {
        if (published is not null)
        {
            published.Status = WorkflowStatus.Archived;
            await _uow.SaveChangesAsync(token);
        }

        draft.Status = WorkflowStatus.Published;
        draft.PublishedAt = _time.GetUtcNow().UtcDateTime;
        workflow.Status = WorkflowStatus.Published;

        await _uow.SaveChangesAsync(token);
    }

    public async Task<ServiceResult<List<WorkflowAvailableFormContract>>> GetAvailableFormsAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<List<WorkflowAvailableFormContract>>();
        if (workflow.OwnerUserId != userId) return NotOwner<List<WorkflowAvailableFormContract>>();

        var draft = await _workflows.GetVersionAsync(workflowId, WorkflowStatus.Draft, cancellationToken);
        var used = (draft?.Nodes ?? []).Select(node => node.FormId).ToHashSet();

        var available = await BuildAvailableFormsAsync(workflowId, userId, workflow.AllowMultipleRuns, used, cancellationToken);

        return new ServiceResult<List<WorkflowAvailableFormContract>>(ServiceStatus.Success, available);
    }

    public async Task<ServiceResult<List<WorkflowAvailableFormContract>>> GetCandidateFormsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var available = await BuildAvailableFormsAsync(Guid.Empty, userId, allowMultipleRuns: false, new HashSet<Guid>(), cancellationToken);

        return new ServiceResult<List<WorkflowAvailableFormContract>>(ServiceStatus.Success, available);
    }

    private async Task<List<WorkflowAvailableFormContract>> BuildAvailableFormsAsync(
        Guid workflowId,
        Guid userId,
        bool allowMultipleRuns,
        IReadOnlySet<Guid> used,
        CancellationToken cancellationToken)
    {
        var candidates = await _workflows.GetOwnedFormsAsync(userId, cancellationToken);
        var formIds = candidates.Select(form => form.Id).ToList();

        var facts = await _workflows.GetNodeFormsAsync(formIds, userId, cancellationToken);
        var clashes = await _workflows.FindFormsInOtherPublishedWorkflowsAsync(workflowId, formIds, cancellationToken);
        var legacyLinked = await _workflows.FindLegacyLinkedFormsAsync(formIds, cancellationToken);

        return candidates
            .Select(form =>
            {
                var reason = FindIneligibilityReason(form.Id, facts, clashes, legacyLinked, allowMultipleRuns);

                return new WorkflowAvailableFormContract(
                    form.Id,
                    form.Title,
                    form.RequiresManualReview,
                    reason is null,
                    reason,
                    used.Contains(form.Id),
                    form.TimeLimitMinutes is > 0 ? form.TimeLimitMinutes : null);
            })
            .ToList();
    }

    /// <summary>
    /// Publish doğrulamasıyla aynı sözlüğü kullanır ki istemci tek bir kod kümesi
    /// için metin yazsın. Sıra, kullanıcının önce düzeltebileceği sebepten başlar.
    /// </summary>
    private static string? FindIneligibilityReason(
        Guid formId,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> facts,
        IReadOnlyDictionary<Guid, string> clashes,
        IReadOnlyCollection<Guid> legacyLinked,
        bool allowMultipleRuns)
    {
        if (!facts.TryGetValue(formId, out var form) || !form.Exists) return "formMissing";
        if (!form.WorkflowOwnerIsFormOwner) return "formNotOwned";
        if (form.AllowAnonymousResponses) return "formAnonymous";
        if (!form.IsOpen) return "formClosed";
        if (allowMultipleRuns && form.HasTimeLimit) return "formTimedRepeatable";
        if (clashes.ContainsKey(formId)) return "formInAnotherWorkflow";
        if (legacyLinked.Contains(formId)) return "formIsLegacyLinked";

        return null;
    }

    public async Task<ServiceResult<WorkflowValidationContract>> ValidateAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowValidationContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowValidationContract>();

        var draft = await _workflows.GetVersionAsync(workflowId, WorkflowStatus.Draft, cancellationToken);
        if (draft is null)
            return new ServiceResult<WorkflowValidationContract>(ServiceStatus.NotAcceptable, Message: "Doğrulanacak taslak yok.");

        var errors = await FindErrorsAsync(workflow, draft, cancellationToken);

        return new ServiceResult<WorkflowValidationContract>(ServiceStatus.Success, ToValidationContract(errors));
    }

    public async Task<ServiceResult<WorkflowValidationContract>> PublishAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowValidationContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowValidationContract>();

        if (workflow.Status == WorkflowStatus.Archived)
            return new ServiceResult<WorkflowValidationContract>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş akış yayınlanamaz.");

        var draft = await _workflows.GetVersionForEditAsync(workflowId, WorkflowStatus.Draft, cancellationToken);
        if (draft is null)
            return new ServiceResult<WorkflowValidationContract>(ServiceStatus.NotAcceptable, Message: "Yayınlanacak taslak yok.");

        var errors = await FindErrorsAsync(workflow, draft, cancellationToken);

        if (errors.Count > 0)
        {
            return new ServiceResult<WorkflowValidationContract>(
                ServiceStatus.NotAcceptable,
                ToValidationContract(errors),
                "Akış yayınlanamadı; tanımda düzeltilmesi gereken noktalar var.");
        }

        var published = await _workflows.GetVersionForEditAsync(workflowId, WorkflowStatus.Published, cancellationToken);

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            await PromoteAsync(workflow, draft, published, token);

            return true;
        }, cancellationToken);

        return new ServiceResult<WorkflowValidationContract>(
            ServiceStatus.Success,
            new WorkflowValidationContract(true, []),
            "Akış yayınlandı.");
    }

    public async Task<ServiceResult<WorkflowSaveContract>> SaveAsync(
        Guid workflowId,
        WorkflowSaveRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowSaveContract>();
        if (workflow.OwnerUserId != userId) return NotOwner<WorkflowSaveContract>();

        if (workflow.Status == WorkflowStatus.Archived)
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş akış düzenlenemez.");

        if (NameError(request.Name) is { } nameError)
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.NotAcceptable, Message: nameError);

        if (request.Definition is null)
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.NotAcceptable, Message: "Akış tanımı gönderilmedi.");

        var forms = request.Forms ?? [];

        if (forms.Any(form => form is null))
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.NotAcceptable, Message: "Kayda içeriği eksik bir form gönderildi.");

        if (forms.DistinctBy(form => form.FormId).Count() != forms.Count)
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.NotAcceptable, Message: "Aynı form kayda birden fazla kez gönderildi.");

        var draft = await PrepareDraftAsync(workflowId, request.Definition, cancellationToken);
        if (draft.Error is not null)
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.NotAcceptable, Message: draft.Error);

        var caller = new StudioCaller(userId, _currentUser.IsPlatformAdmin);
        var pending = new Dictionary<Guid, FormUpsertRequest>();

        foreach (var form in forms)
        {
            var preview = await _publishing.PreviewAsync(form.FormId, form.WorkingVersion, caller, cancellationToken);
            if (preview.Data is not { } document) return FormRejected(preview.Status, preview.Message, form.FormId);

            pending[form.FormId] = FormPublishing.UpsertOf(document);
        }

        var stored = await _workflows.GetNodeFormsAsync([.. draft.Nodes.Select(node => node.FormId).Distinct()], workflow.OwnerUserId, cancellationToken);
        var facts = stored.ToDictionary(
            entry => entry.Key,
            entry => pending.TryGetValue(entry.Key, out var next) ? AsSaved(entry.Value, next) : entry.Value);

        var errors = await FindErrorsAsync(workflow.Id, request.AllowMultipleRuns, draft.Nodes, draft.Transitions, facts, cancellationToken);

        if (errors.Count > 0)
        {
            return new ServiceResult<WorkflowSaveContract>(
                ServiceStatus.NotAcceptable,
                new WorkflowSaveContract(null, [], ToValidationContract(errors)),
                "Akış kaydedilemedi; tanımda düzeltilmesi gereken noktalar var.");
        }

        var stepFormIds = draft.Nodes.Select(node => node.FormId).ToHashSet();
        var guestUploadsEnabled = _guestUploads.Capability is not null;

        foreach (var form in forms)
        {
            if (FindFormError(pending[form.FormId], stepFormIds.Contains(form.FormId), guestUploadsEnabled) is { } formError)
                return FormRejected(formError.Status, formError.Message, form.FormId);
        }

        var published = await _workflows.GetVersionForEditAsync(workflowId, WorkflowStatus.Published, cancellationToken);
        var graphChanged = published is null || !HasSameGraph(draft.Nodes, draft.Transitions, published);

        if (graphChanged && draft.IsNew) _workflows.Add(draft.Version);

        var staged = new List<StagedPublication>();

        try
        {
            await _uow.ExecuteInTransactionAsync(async token =>
            {
                staged.Clear();

                workflow.Name = request.Name.Trim();
                workflow.Description = request.Description;
                workflow.AllowMultipleRuns = request.AllowMultipleRuns;

                if (graphChanged)
                {
                    await ReplaceDraftGraphAsync(draft, token);
                    await PromoteAsync(workflow, draft.Version, published, token);
                }
                else
                {
                    if (!draft.IsNew) RemoveDraft(draft.Version);

                    await _uow.SaveChangesAsync(token);
                }

                foreach (var form in forms)
                {
                    var stage = await _publishing.StageAsync(form.FormId, form.WorkingVersion, caller, token);
                    if (stage.Data is null) throw new SaveRejected(FormRejected(stage.Status, stage.Message, form.FormId));

                    staged.Add(stage.Data);
                }

                await _uow.SaveChangesAsync(token);

                return true;
            }, cancellationToken);
        }
        catch (SaveRejected rejected)
        {
            return rejected.Result;
        }
        catch (StorageConflictException)
        {
            return new ServiceResult<WorkflowSaveContract>(ServiceStatus.Conflict, Message: "Akış ya da formlarından biri aynı anda başka bir yerden değişti; tekrar kaydedin.");
        }

        var saved = new List<FormContract>();

        foreach (var publication in staged)
        {
            await _publishing.CompleteAsync(publication, userId, cancellationToken);
            saved.Add(await _publishing.ContractOfAsync(publication.Form, publication.Role, cancellationToken));
        }

        var contract = await BuildContractAsync(workflow, userId, cancellationToken);

        return new ServiceResult<WorkflowSaveContract>(
            ServiceStatus.Success,
            new WorkflowSaveContract(contract.Data, saved),
            "Akış kaydedildi.");
    }

    private static WorkflowNodeForm AsSaved(WorkflowNodeForm stored, FormUpsertRequest next) => stored with
    {
        IsOpen = next.Status == FormStatus.Open,
        AllowAnonymousResponses = next.AllowAnonymousResponses,
        QuestionIds = [.. (next.Schema ?? []).Select(item => item.Id)],
        HasTimeLimit = next.TimeLimitMinutes is > 0
    };

    private static ServiceResult<bool>? FindFormError(FormUpsertRequest form, bool isStep, bool guestUploadsEnabled)
    {
        var upsert = FormValidator.ValidateUpsert(form, form.Schema ?? [], guestUploadsEnabled);
        if (upsert.Status.IsFailure()) return upsert;

        var task = string.IsNullOrWhiteSpace(form.Task?.Content) ? null : form.Task;
        var timeLimit = form.TimeLimitMinutes is > 0 ? form.TimeLimitMinutes : null;

        var timing = FormValidator.ValidateTiming(form.AllowAnonymousResponses, !isStep && form.AllowMultipleResponses, task, timeLimit);

        return timing.Status.IsFailure() ? timing : null;
    }

    private static ServiceResult<WorkflowSaveContract> FormRejected(ServiceStatus status, string? message, Guid formId) =>
        new(status, new WorkflowSaveContract(null, [], FailedFormId: formId), message);

    private sealed class SaveRejected(ServiceResult<WorkflowSaveContract> result) : Exception(result.Message)
    {
        public ServiceResult<WorkflowSaveContract> Result { get; } = result;
    }

    public async Task<ServiceResult<bool>> ArchiveAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<bool>();

        // Silinmiş kullanıcıya kalan akışı devralan olmazsa platform admini arşivleyebilir.
        var adminMayArchive = workflow.OwnerUserId == DeletedUser.Id && _currentUser.IsPlatformAdmin;
        if (workflow.OwnerUserId != userId && !adminMayArchive) return NotOwner<bool>();

        var now = _time.GetUtcNow().UtcDateTime;

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            await _instances.TouchActiveRunsAsync(workflow.Id, now, token);

            await _instances.StopRunsAwaitingApplicantAsync(workflow.Id, now, token);
            await _workflows.CloseStepFormsAsync(workflow.Id, now, token);
            await _workflows.ArchiveAsync(workflow.Id, now, token);

            return true;
        }, cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, true, "Akış arşivlendi; devam eden başvurular durduruldu ve adım formları kapatıldı.");
    }

    private async Task<IReadOnlyList<WorkflowValidationError>> FindErrorsAsync(
        FormWorkflow workflow,
        FormWorkflowVersion draft,
        CancellationToken cancellationToken)
    {
        var nodes = draft.Nodes.ToList();
        var forms = await _workflows.GetNodeFormsAsync([.. nodes.Select(node => node.FormId).Distinct()], workflow.OwnerUserId, cancellationToken);

        return await FindErrorsAsync(workflow.Id, workflow.AllowMultipleRuns, nodes, draft.Transitions.ToList(), forms, cancellationToken);
    }

    /// <summary>
    /// Graf kurallarına, yalnızca depolamadan bilinebilen iki kural eklenir: aynı
    /// formun iki yayında birden yer alması ve legacy bağlı form akışıyla çakışma.
    /// </summary>
    private async Task<IReadOnlyList<WorkflowValidationError>> FindErrorsAsync(
        Guid workflowId,
        bool allowMultipleRuns,
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyList<FormWorkflowTransition> transitions,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> forms,
        CancellationToken cancellationToken)
    {
        var formIds = nodes.Select(node => node.FormId).Distinct().ToList();
        var errors = WorkflowGraphValidator.Validate(nodes, transitions, forms).ToList();

        if (formIds.Count == 0) return errors;

        var clashes = await _workflows.FindFormsInOtherPublishedWorkflowsAsync(workflowId, formIds, cancellationToken);

        foreach (var node in nodes.Where(node => clashes.ContainsKey(node.FormId)))
        {
            errors.Add(new WorkflowValidationError(
                "formInAnotherWorkflow",
                $"'{node.NodeKey}' adımının formu '{clashes[node.FormId]}' akışında da yayında; bir form aynı anda tek akışta yer alabilir.",
                node.NodeKey));
        }

        var legacyLinked = await _workflows.FindLegacyLinkedFormsAsync(formIds, cancellationToken);

        foreach (var node in nodes.Where(node => legacyLinked.Contains(node.FormId)))
        {
            errors.Add(new WorkflowValidationError(
                "formIsLegacyLinked",
                $"'{node.NodeKey}' adımının formu eski bağlı form akışının parçası; önce o bağlantıyı kaldırın.",
                node.NodeKey));
        }

        if (allowMultipleRuns)
        {
            foreach (var node in nodes.Where(node => forms.TryGetValue(node.FormId, out var form) && form.HasTimeLimit))
            {
                errors.Add(new WorkflowValidationError(
                    "formTimedRepeatable",
                    $"'{node.NodeKey}' adımının formunda kişisel süre var; tekrar başlatılabilen akışlarda kişisel süre kullanılamaz.",
                    node.NodeKey));
            }
        }

        return errors;
    }

    private static (List<FormWorkflowNode> Nodes, List<FormWorkflowTransition> Transitions, string? Error) BuildGraph(
        Guid versionId,
        WorkflowDefinitionRequest request,
        IReadOnlyDictionary<Guid, WorkflowFormHeader> defaults)
    {
        var nodes = new List<FormWorkflowNode>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenForms = new HashSet<Guid>();

        foreach (var node in request.Nodes)
        {
            var key = node.NodeKey?.Trim() ?? string.Empty;

            if (key.Length == 0) return ([], [], "Her adımın bir anahtarı olmalıdır.");
            if (key.Length > WorkflowLimits.MaxNodeKeyLength) return ([], [], $"Adım anahtarı en fazla {WorkflowLimits.MaxNodeKeyLength} karakter olabilir.");
            if (!seenKeys.Add(key)) return ([], [], $"'{key}' adım anahtarı birden fazla kez kullanılmış.");
            if (!seenForms.Add(node.FormId)) return ([], [], "Aynı form akışta birden fazla adımda kullanılamaz.");

            nodes.Add(new FormWorkflowNode
            {
                WorkflowVersionId = versionId,
                FormId = node.FormId,
                NodeKey = key,
                IsStart = node.IsStart,
                RequiresManualReview = node.RequiresManualReview
                    ?? (defaults.TryGetValue(node.FormId, out var header) && header.RequiresManualReview),
                PositionX = node.Position?.X,
                PositionY = node.Position?.Y
            });
        }

        var nodesByKey = nodes.ToDictionary(node => node.NodeKey, StringComparer.OrdinalIgnoreCase);
        var transitions = new List<FormWorkflowTransition>();
        var priorities = new HashSet<(Guid, WorkflowTransitionTrigger, int)>();

        foreach (var transition in request.Transitions)
        {
            if (!nodesByKey.TryGetValue(transition.SourceNodeKey?.Trim() ?? string.Empty, out var source))
                return ([], [], $"'{transition.SourceNodeKey}' adımı akışta bulunmuyor.");

            if (!Enum.IsDefined(transition.Trigger))
                return ([], [], $"'{source.NodeKey}' adımına bilinmeyen bir tetikle yönlendirme gönderildi.");

            if (!priorities.Add((source.Id, transition.Trigger, transition.Priority)))
                return ([], [], $"'{source.NodeKey}' adımında aynı tetik için iki yönlendirme aynı önceliği paylaşıyor.");

            FormWorkflowNode? target = null;
            var targetKey = transition.TargetNodeKey?.Trim();

            if (!string.IsNullOrEmpty(targetKey) && !nodesByKey.TryGetValue(targetKey, out target))
                return ([], [], $"'{targetKey}' adımı akışta bulunmuyor.");

            transitions.Add(new FormWorkflowTransition
            {
                WorkflowVersionId = versionId,
                SourceNodeId = source.Id,
                TargetNodeId = target?.Id,
                Trigger = transition.Trigger,
                Condition = transition.Condition,
                Priority = transition.Priority
            });
        }

        return (nodes, transitions, null);
    }

    private static bool HasSameGraph(
        IReadOnlyCollection<FormWorkflowNode> nodes,
        IReadOnlyCollection<FormWorkflowTransition> transitions,
        FormWorkflowVersion version) =>
        NodeShapes(nodes).SequenceEqual(NodeShapes(version.Nodes))
        && TransitionShapes(nodes, transitions).SequenceEqual(TransitionShapes(version.Nodes, version.Transitions));

    private static List<NodeShape> NodeShapes(IEnumerable<FormWorkflowNode> nodes) =>
        nodes
            .Select(node => new NodeShape(node.NodeKey, node.FormId, node.IsStart, node.RequiresManualReview, node.PositionX, node.PositionY))
            .OrderBy(shape => shape.Key, StringComparer.Ordinal)
            .ToList();

    private static List<TransitionShape> TransitionShapes(IEnumerable<FormWorkflowNode> nodes, IEnumerable<FormWorkflowTransition> transitions)
    {
        var keysById = nodes.ToDictionary(node => node.Id, node => node.NodeKey);

        return transitions
            .Select(transition => new TransitionShape(
                keysById[transition.SourceNodeId],
                transition.TargetNodeId is { } targetId ? keysById[targetId] : null,
                transition.Trigger,
                transition.Priority,
                JsonSerializer.Serialize(transition.Condition)))
            .OrderBy(shape => shape.Source, StringComparer.Ordinal)
            .ThenBy(shape => shape.Trigger)
            .ThenBy(shape => shape.Priority)
            .ThenBy(shape => shape.Target, StringComparer.Ordinal)
            .ThenBy(shape => shape.Condition, StringComparer.Ordinal)
            .ToList();
    }

    private sealed record NodeShape(string Key, Guid FormId, bool IsStart, bool RequiresManualReview, int? X, int? Y);

    private sealed record TransitionShape(string Source, string? Target, WorkflowTransitionTrigger Trigger, int Priority, string Condition);

    private async Task<ServiceResult<WorkflowContract>> BuildContractAsync(FormWorkflow workflow, Guid userId, CancellationToken cancellationToken)
    {
        var draft = await _workflows.GetVersionAsync(workflow.Id, WorkflowStatus.Draft, cancellationToken);
        var published = await _workflows.GetVersionAsync(workflow.Id, WorkflowStatus.Published, cancellationToken);

        var formIds = (draft?.Nodes ?? []).Concat(published?.Nodes ?? [])
            .Select(node => node.FormId)
            .Distinct()
            .ToList();

        var headers = formIds.Count == 0
            ? new Dictionary<Guid, WorkflowFormHeader>()
            : await _workflows.GetFormHeadersAsync(formIds, cancellationToken);

        var owner = await _userService.GetUserAsync(workflow.OwnerUserId, cancellationToken)
            ?? new UserContract(workflow.OwnerUserId, null, null, null);

        // Taslak yoksa yayındaki sürüm doğrulanır; editör rozeti ikinci bir çağrı beklemesin.
        var validated = draft ?? published;

        var validation = validated is null
            ? new WorkflowValidationContract(false, [])
            : ToValidationContract(await FindErrorsAsync(workflow, validated, cancellationToken));

        var contract = new WorkflowContract(
            workflow.Id,
            workflow.Name,
            workflow.Description,
            workflow.Status,
            workflow.AllowMultipleRuns,
            workflow.Intake,
            await _instances.CountActiveAsync(workflow.Id, cancellationToken),
            owner,
            workflow.OwnerUserId == userId,
            ToVersionContract(draft, headers),
            ToVersionContract(published, headers),
            validation,
            workflow.CreatedAt,
            workflow.UpdatedAt,
            workflow.IntakeClosesAt);

        return new ServiceResult<WorkflowContract>(ServiceStatus.Success, contract);
    }

    private static WorkflowVersionContract? ToVersionContract(FormWorkflowVersion? version, IReadOnlyDictionary<Guid, WorkflowFormHeader> headers)
    {
        if (version is null) return null;

        var keysById = version.Nodes.ToDictionary(node => node.Id, node => node.NodeKey);

        var nodes = version.Nodes
            .Select(node =>
            {
                var found = headers.TryGetValue(node.FormId, out var header);

                return new WorkflowNodeContract(
                    node.NodeKey,
                    node.FormId,
                    found ? header!.Title : "(silinmiş form)",
                    node.RequiresManualReview,
                    node.IsStart,
                    node.PositionX is { } x && node.PositionY is { } y
                        ? new WorkflowNodePositionContract(x, y)
                        : null,
                    found ? header!.TimeLimitMinutes : null);
            })
            .ToList();

        var transitions = version.Transitions
            .OrderBy(transition => transition.Priority)
            .Select(transition => new WorkflowTransitionContract(
                keysById[transition.SourceNodeId],
                transition.TargetNodeId.HasValue ? keysById[transition.TargetNodeId.Value] : null,
                transition.Trigger,
                transition.Condition,
                transition.Priority))
            .ToList();

        return new WorkflowVersionContract(version.Id, version.Version, version.Status, version.PublishedAt, nodes, transitions);
    }

    private static WorkflowFormRefContract? ToFormRef(Guid? formId, IReadOnlyDictionary<Guid, WorkflowFormHeader> headers) =>
        formId is { } id
            ? new WorkflowFormRefContract(id, headers.TryGetValue(id, out var header) ? header.Title : "(silinmiş form)")
            : null;

    private static WorkflowValidationContract ToValidationContract(IReadOnlyList<WorkflowValidationError> errors) =>
        new(errors.Count == 0, [.. errors.Select(error => new WorkflowValidationErrorContract(error.Code, error.Message, error.NodeKey))]);

    public async Task<ServiceResult<bool>> VoidRunAsync(
        Guid instanceId,
        WorkflowRunVoidRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        if (note?.Length > FormWorkflowInstance.VoidNoteMaxLength)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: $"Not en fazla {FormWorkflowInstance.VoidNoteMaxLength} karakter olabilir.");

        var access = await _instances.GetRunAccessAsync(instanceId, userId, cancellationToken);
        var allowed = access is { IsWorkflowOwner: true }
            || access?.StepFormRoles.Any(role => FormAccess.Allows(role, _currentUser.IsPlatformAdmin, FormAction.VoidApplication)) == true;

        if (!allowed)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu başvuruyu yeniden açma yetkiniz yok.");

        var voided = await _runtime.VoidAsync(instanceId, userId, note, cancellationToken);
        if (voided.Status.IsFailure() || voided.Data is null)
            return new ServiceResult<bool>(voided.Status, Message: voided.Message);

        await _mailNotifier.NotifyRunReopenedAsync(voided.Data, cancellationToken);

        return new ServiceResult<bool>(
            ServiceStatus.Success,
            true,
            voided.Data.WasActive ? "Başvuru baştan başlatıldı." : "Adaya yeniden başvuru hakkı verildi.");
    }

    private static ServiceResult<T> NotFound<T>() => new(ServiceStatus.NotFound, Message: "Akış bulunamadı.");

    private static ServiceResult<T> NotOwner<T>() => new(ServiceStatus.NotAuthorized, Message: "Bu akış üzerinde yetkiniz yok.");

    private static string? NameError(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Akış adı boş olamaz."
        : name.Trim().Length > FormWorkflow.NameMaxLength ? $"Akış adı en fazla {FormWorkflow.NameMaxLength} karakter olabilir."
        : null;
}
