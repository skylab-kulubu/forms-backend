using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public class FormWorkflowService : IFormWorkflowService
{
    private readonly IFormWorkflowRepository _workflows;
    private readonly IFormsUnitOfWork _uow;
    private readonly IExternalUserService _userService;
    private readonly ICurrentUserService _currentUserService;

    public FormWorkflowService(
        IFormWorkflowRepository workflows,
        IFormsUnitOfWork uow,
        IExternalUserService userService,
        ICurrentUserService currentUserService)
    {
        _workflows = workflows;
        _uow = uow;
        _userService = userService;
        _currentUserService = currentUserService;
    }

    public async Task<ServiceResult<WorkflowContract>> CreateAsync(
        WorkflowUpsertRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Akış adı boş olamaz.");

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

        return await BuildContractAsync(workflow, cancellationToken);
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

        if (string.IsNullOrWhiteSpace(request.Name))
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: "Akış adı boş olamaz.");

        workflow.Name = request.Name.Trim();
        workflow.Description = request.Description;
        workflow.AllowMultipleRuns = request.AllowMultipleRuns;

        await _uow.SaveChangesAsync(cancellationToken);

        return await BuildContractAsync(workflow, cancellationToken);
    }

    public async Task<ServiceResult<WorkflowContract>> GetAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<WorkflowContract>();

        if (workflow.OwnerUserId != userId && !await IsPlatformAdminAsync(cancellationToken))
            return NotOwner<WorkflowContract>();

        return await BuildContractAsync(workflow, cancellationToken);
    }

    public async Task<ServiceResult<List<WorkflowSummaryContract>>> GetOwnedAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflows = await _workflows.GetOwnedWorkflowsAsync(userId, cancellationToken);

        var startFormIds = workflows
            .Where(workflow => workflow.StartFormId.HasValue)
            .Select(workflow => workflow.StartFormId!.Value)
            .Distinct()
            .ToList();

        var headers = await _workflows.GetFormHeadersAsync(startFormIds, cancellationToken);

        var summaries = workflows
            .Select(workflow => new WorkflowSummaryContract(
                workflow.Id,
                workflow.Name,
                workflow.Status,
                workflow.AllowMultipleRuns,
                ToFormRef(workflow.StartFormId, headers),
                workflow.NodeCount,
                workflow.PublishedVersion,
                workflow.HasUnpublishedChanges,
                workflow.UpdatedAt))
            .ToList();

        return new ServiceResult<List<WorkflowSummaryContract>>(ServiceStatus.Success, summaries);
    }

    public async Task<ServiceResult<List<WorkflowVersionSummaryContract>>> GetVersionsAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<List<WorkflowVersionSummaryContract>>();

        if (workflow.OwnerUserId != userId && !await IsPlatformAdminAsync(cancellationToken))
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

        var draft = await _workflows.GetVersionForEditAsync(workflowId, WorkflowStatus.Draft, cancellationToken);

        if (draft is null)
        {
            // Yayınlanmış version değişmez; düzenleme yeni bir taslak üretir.
            draft = new FormWorkflowVersion
            {
                WorkflowId = workflowId,
                Version = await _workflows.GetNextVersionNumberAsync(workflowId, cancellationToken),
                Status = WorkflowStatus.Draft
            };

            _workflows.Add(draft);
        }

        var build = BuildGraph(draft.Id, request);
        if (build.Error is not null)
            return new ServiceResult<WorkflowContract>(ServiceStatus.NotAcceptable, Message: build.Error);

        // Silme ve ekleme tek kayıt işleminde gönderilirse EF ekleme komutlarını
        // önce yazabilir ve node anahtarı üzerindeki tekil indeks ihlal edilir.
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            _workflows.RemoveRange(draft.Transitions.ToList());
            _workflows.RemoveRange(draft.Nodes.ToList());

            await _uow.SaveChangesAsync(token);

            _workflows.AddRange(build.Nodes);
            _workflows.AddRange(build.Transitions);

            await _uow.SaveChangesAsync(token);

            return true;
        }, cancellationToken);

        return await BuildContractAsync(workflow, cancellationToken);
    }

    public async Task<ServiceResult<List<WorkflowAvailableFormContract>>> GetAvailableFormsAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<List<WorkflowAvailableFormContract>>();
        if (workflow.OwnerUserId != userId) return NotOwner<List<WorkflowAvailableFormContract>>();

        var candidates = await _workflows.GetOwnedFormsAsync(userId, cancellationToken);
        var formIds = candidates.Select(form => form.Id).ToList();

        var facts = await _workflows.GetNodeFormsAsync(formIds, userId, cancellationToken);
        var clashes = await _workflows.FindFormsInOtherPublishedWorkflowsAsync(workflowId, formIds, cancellationToken);
        var legacyLinked = await _workflows.FindLegacyLinkedFormsAsync(formIds, cancellationToken);

        var draft = await _workflows.GetVersionAsync(workflowId, WorkflowStatus.Draft, cancellationToken);
        var used = (draft?.Nodes ?? []).Select(node => node.FormId).ToHashSet();

        var available = candidates
            .Select(form =>
            {
                var reason = FindIneligibilityReason(form.Id, facts, clashes, legacyLinked);

                return new WorkflowAvailableFormContract(
                    form.Id,
                    form.Title,
                    reason is null,
                    reason,
                    used.Contains(form.Id));
            })
            .ToList();

        return new ServiceResult<List<WorkflowAvailableFormContract>>(ServiceStatus.Success, available);
    }

    /// <summary>
    /// Publish doğrulamasıyla aynı sözlüğü kullanır ki istemci tek bir kod kümesi
    /// için metin yazsın. Sıra, kullanıcının önce düzeltebileceği sebepten başlar.
    /// </summary>
    private static string? FindIneligibilityReason(
        Guid formId,
        IReadOnlyDictionary<Guid, WorkflowNodeForm> facts,
        IReadOnlyDictionary<Guid, string> clashes,
        IReadOnlyCollection<Guid> legacyLinked)
    {
        if (!facts.TryGetValue(formId, out var form) || !form.Exists) return "formMissing";
        if (!form.WorkflowOwnerIsFormOwner) return "formNotOwned";
        if (form.AllowAnonymousResponses) return "formAnonymous";
        if (!form.IsOpen) return "formClosed";
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

        // Eskiyi arşivlemek ile yeniyi yayınlamak tek kayıt işleminde gönderilirse
        // iki yayınlanmış version aynı anda var olur ve tekil indeks ihlal edilir.
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            if (published is not null)
            {
                published.Status = WorkflowStatus.Archived;
                await _uow.SaveChangesAsync(token);
            }

            draft.Status = WorkflowStatus.Published;
            draft.PublishedAt = DateTime.UtcNow;
            workflow.Status = WorkflowStatus.Published;

            await _uow.SaveChangesAsync(token);

            return true;
        }, cancellationToken);

        return new ServiceResult<WorkflowValidationContract>(
            ServiceStatus.Success,
            new WorkflowValidationContract(true, []),
            "Akış yayınlandı.");
    }

    public async Task<ServiceResult<bool>> ArchiveAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, cancellationToken);
        if (workflow is null) return NotFound<bool>();
        if (workflow.OwnerUserId != userId) return NotOwner<bool>();

        workflow.Status = WorkflowStatus.Archived;

        await _uow.SaveChangesAsync(cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, true, "Akış arşivlendi; devam eden başvurular etkilenmedi.");
    }

    /// <summary>
    /// Graf kurallarına, yalnızca depolamadan bilinebilen iki kural eklenir: aynı
    /// formun iki yayında birden yer alması ve legacy bağlı form akışıyla çakışma.
    /// </summary>
    private async Task<IReadOnlyList<WorkflowValidationError>> FindErrorsAsync(
        FormWorkflow workflow,
        FormWorkflowVersion draft,
        CancellationToken cancellationToken)
    {
        var nodes = draft.Nodes.ToList();
        var transitions = draft.Transitions.ToList();
        var formIds = nodes.Select(node => node.FormId).Distinct().ToList();

        var forms = await _workflows.GetNodeFormsAsync(formIds, workflow.OwnerUserId, cancellationToken);
        var errors = WorkflowGraphValidator.Validate(nodes, transitions, forms).ToList();

        if (formIds.Count == 0) return errors;

        var clashes = await _workflows.FindFormsInOtherPublishedWorkflowsAsync(workflow.Id, formIds, cancellationToken);

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

        return errors;
    }

    private static (List<FormWorkflowNode> Nodes, List<FormWorkflowTransition> Transitions, string? Error) BuildGraph(
        Guid versionId,
        WorkflowDefinitionRequest request)
    {
        var nodes = new List<FormWorkflowNode>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in request.Nodes)
        {
            var key = node.NodeKey?.Trim() ?? string.Empty;

            if (key.Length == 0) return ([], [], "Her adımın bir anahtarı olmalıdır.");
            if (!seenKeys.Add(key)) return ([], [], $"'{key}' adım anahtarı birden fazla kez kullanılmış.");

            nodes.Add(new FormWorkflowNode
            {
                WorkflowVersionId = versionId,
                FormId = node.FormId,
                NodeKey = key,
                IsStart = node.IsStart,
                PositionX = node.Position?.X,
                PositionY = node.Position?.Y
            });
        }

        var nodesByKey = nodes.ToDictionary(node => node.NodeKey, StringComparer.OrdinalIgnoreCase);
        var transitions = new List<FormWorkflowTransition>();

        foreach (var transition in request.Transitions)
        {
            if (!nodesByKey.TryGetValue(transition.SourceNodeKey?.Trim() ?? string.Empty, out var source))
                return ([], [], $"'{transition.SourceNodeKey}' adımı akışta bulunmuyor.");

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

    private async Task<ServiceResult<WorkflowContract>> BuildContractAsync(FormWorkflow workflow, CancellationToken cancellationToken)
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
            owner,
            ToVersionContract(draft, headers),
            ToVersionContract(published, headers),
            validation,
            workflow.CreatedAt,
            workflow.UpdatedAt);

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
                    found && header!.RequiresManualReview,
                    node.IsStart,
                    node.PositionX is { } x && node.PositionY is { } y
                        ? new WorkflowNodePositionContract(x, y)
                        : null);
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

    private Task<bool> IsPlatformAdminAsync(CancellationToken cancellationToken) =>
        _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken);

    private static ServiceResult<T> NotFound<T>() => new(ServiceStatus.NotFound, Message: "Akış bulunamadı.");

    private static ServiceResult<T> NotOwner<T>() => new(ServiceStatus.NotAuthorized, Message: "Bu akış üzerinde yetkiniz yok.");
}
