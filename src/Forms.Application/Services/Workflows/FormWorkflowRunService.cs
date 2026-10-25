using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public class FormWorkflowRunService : IFormWorkflowRunService
{
    private readonly IFormWorkflowRepository _workflows;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly IExternalUserService _userService;
    private readonly ICurrentUser _currentUser;

    public FormWorkflowRunService(
        IFormWorkflowRepository workflows,
        IFormWorkflowInstanceRepository instances,
        IExternalUserService userService,
        ICurrentUser currentUser)
    {
        _workflows = workflows;
        _instances = instances;
        _userService = userService;
        _currentUser = currentUser;
    }

    public async Task<ServiceResult<WorkflowRunOverviewContract>> GetOverviewAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await FindAccessProblemAsync(workflowId, userId, cancellationToken) is { } problem)
            return new ServiceResult<WorkflowRunOverviewContract>(problem.Status, Message: problem.Message);

        var overviews = await GetOverviewsAsync([workflowId], cancellationToken);

        return new ServiceResult<WorkflowRunOverviewContract>(ServiceStatus.Success, overviews[workflowId]);
    }

    public async Task<ServiceResult<PagedResult<WorkflowRunContract>>> GetRunsAsync(
        Guid workflowId,
        GetWorkflowRunsRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await FindAccessProblemAsync(workflowId, userId, cancellationToken) is { } problem)
            return new ServiceResult<PagedResult<WorkflowRunContract>>(problem.Status, Message: problem.Message);

        var page = await _instances.GetRunsAsync(workflowId, request, DateTime.UtcNow, cancellationToken);

        var users = await _userService.GetUsersAsync(page.Items.Select(run => run.UserId), cancellationToken);
        var usersById = users.DistinctBy(user => user.Id).ToDictionary(user => user.Id);

        var formIds = page.Items.Select(run => run.FormId).Distinct().ToList();
        var forms = formIds.Count == 0
            ? new Dictionary<Guid, WorkflowFormHeader>()
            : await _workflows.GetFormHeadersAsync(formIds, cancellationToken);

        var runs = page.Items
            .Select(run => new WorkflowRunContract(
                run.InstanceId,
                usersById.GetValueOrDefault(run.UserId) ?? new UserContract(run.UserId, null, "??", null),
                WorkflowRunOverviewBuilder.StateOf(run.Status, run.Outcome, run.HasResponse, run.DeadlineAt is not null, run.Round),
                run.NodeKey,
                run.FormId,
                WorkflowRunOverviewBuilder.TitleOf(forms, run.FormId),
                run.Sequence,
                run.Round,
                run.StartedAt,
                run.StepStartedAt,
                run.DeadlineAt,
                run.CompletedAt,
                run.LatestResponseId is { } responseId && run.LatestResponseFormId is { } responseFormId
                    ? new WorkflowRunResponseContract(responseId, responseFormId)
                    : null))
            .ToList();

        return new ServiceResult<PagedResult<WorkflowRunContract>>(
            ServiceStatus.Success,
            new PagedResult<WorkflowRunContract>(runs, page.TotalCount, page.Page, page.PageSize));
    }

    public async Task<IReadOnlyDictionary<Guid, WorkflowRunOverviewContract>> GetOverviewsAsync(
        IReadOnlyCollection<Guid> workflowIds,
        CancellationToken cancellationToken = default)
    {
        if (workflowIds.Count == 0) return new Dictionary<Guid, WorkflowRunOverviewContract>();

        var definitions = await _workflows.GetCurrentDefinitionsAsync(workflowIds, cancellationToken);
        var openSteps = await _instances.GetOpenStepsAsync(workflowIds, DateTime.UtcNow, cancellationToken);
        var tallies = await _instances.CountRunsAsync(workflowIds, cancellationToken);

        var formIds = definitions.Values
            .SelectMany(definition => definition.Nodes.Select(node => node.FormId))
            .Concat(openSteps.Select(step => step.FormId))
            .Distinct()
            .ToList();

        var forms = formIds.Count == 0
            ? new Dictionary<Guid, WorkflowFormHeader>()
            : await _workflows.GetFormHeadersAsync(formIds, cancellationToken);

        return workflowIds
            .Distinct()
            .ToDictionary(
                workflowId => workflowId,
                workflowId => WorkflowRunOverviewBuilder.Build(
                    definitions.GetValueOrDefault(workflowId),
                    [.. openSteps.Where(step => step.WorkflowId == workflowId)],
                    [.. tallies.Where(tally => tally.WorkflowId == workflowId)],
                    forms));
    }

    private async Task<(ServiceStatus Status, string Message)?> FindAccessProblemAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var workflow = await _workflows.GetAsync(workflowId, cancellationToken);
        if (workflow is null) return (ServiceStatus.NotFound, "Akış bulunamadı.");

        if (workflow.OwnerUserId != userId && !_currentUser.IsPlatformAdmin)
            return (ServiceStatus.NotAuthorized, "Bu akış üzerinde yetkiniz yok.");

        return null;
    }
}
