using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public static class WorkflowRunOverviewBuilder
{
    private static readonly WorkflowRunFinishedContract NoEnds = new(0, 0, 0, 0, 0);

    public static WorkflowRunOverviewContract Build(
        WorkflowDefinition? definition,
        IReadOnlyCollection<WorkflowOpenStepFact> openSteps,
        IReadOnlyCollection<WorkflowRunTally> tallies,
        IReadOnlyCollection<WorkflowRouteTally> routes,
        IReadOnlyCollection<WorkflowEndTally> ends,
        IReadOnlyDictionary<Guid, WorkflowFormHeader> forms)
    {
        var openByNode = openSteps
            .GroupBy(step => step.NodeKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var endsByNode = ends
            .GroupBy(end => end.NodeKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => FinishedOf(group.Select(end => (end.Status, end.Outcome, end.Count))), StringComparer.Ordinal);

        var ordered = definition is null ? [] : WorkflowStepOrder.Arrange(definition.Nodes, definition.Transitions);
        var current = ordered.Select(entry => entry.Node.NodeKey).ToHashSet(StringComparer.Ordinal);

        var steps = ordered
            .Select(entry => ToStep(
                entry.Node.NodeKey,
                entry.Node.FormId,
                entry.Node.IsStart,
                entry.Node.RequiresManualReview,
                entry.Depth,
                isRemoved: false,
                openByNode.GetValueOrDefault(entry.Node.NodeKey) ?? [],
                endsByNode.GetValueOrDefault(entry.Node.NodeKey) ?? NoEnds,
                forms))
            .Concat(openByNode
                .Where(group => !current.Contains(group.Key))
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => ToStep(
                    group.Key,
                    group.Value[0].FormId,
                    isStart: false,
                    group.Value[0].RequiresManualReview,
                    depth: 0,
                    isRemoved: true,
                    group.Value,
                    endsByNode.GetValueOrDefault(group.Key) ?? NoEnds,
                    forms)))
            .ToList();

        return new WorkflowRunOverviewContract(
            tallies.Where(tally => tally.Status == WorkflowInstanceStatus.Active).Sum(tally => tally.Count),
            openSteps.Count(step => StateOf(step) == WorkflowRunState.AwaitingReview),
            steps,
            FinishedOf(tallies.Select(tally => (tally.Status, tally.Outcome, tally.Count))),
            [.. routes
                .OrderBy(route => route.SourceNodeKey, StringComparer.Ordinal)
                .ThenBy(route => route.Trigger)
                .ThenBy(route => route.TargetNodeKey, StringComparer.Ordinal)
                .Select(route => new WorkflowRunRouteContract(route.SourceNodeKey, route.Trigger, route.TargetNodeKey, route.Count))]);
    }

    public static WorkflowRunState StateOf(
        WorkflowInstanceStatus status,
        WorkflowInstanceOutcome outcome,
        bool hasResponse,
        bool timerRunning,
        int round) => status switch
    {
        WorkflowInstanceStatus.Active when hasResponse => WorkflowRunState.AwaitingReview,
        WorkflowInstanceStatus.Active when timerRunning => WorkflowRunState.TimerRunning,
        WorkflowInstanceStatus.Active when round > 1 => WorkflowRunState.RevisionRequested,
        WorkflowInstanceStatus.Active => WorkflowRunState.AwaitingApplicant,
        WorkflowInstanceStatus.Completed => WorkflowRunState.Completed,
        WorkflowInstanceStatus.Voided => WorkflowRunState.Voided,
        _ when outcome == WorkflowInstanceOutcome.Declined => WorkflowRunState.Declined,
        _ when outcome == WorkflowInstanceOutcome.TimedOut => WorkflowRunState.TimedOut,
        _ => WorkflowRunState.Stopped
    };

    public static string TitleOf(IReadOnlyDictionary<Guid, WorkflowFormHeader> forms, Guid formId) =>
        forms.TryGetValue(formId, out var form) ? form.Title : "(silinmiş form)";

    private static WorkflowRunState StateOf(WorkflowOpenStepFact step) =>
        StateOf(WorkflowInstanceStatus.Active, WorkflowInstanceOutcome.None, step.HasResponse, step.TimerRunning, step.Round);

    private static WorkflowRunFinishedContract FinishedOf(IEnumerable<(WorkflowInstanceStatus Status, WorkflowInstanceOutcome Outcome, int Count)> runs)
    {
        var byState = runs
            .Where(run => run.Status != WorkflowInstanceStatus.Active)
            .GroupBy(run => StateOf(run.Status, run.Outcome, hasResponse: false, timerRunning: false, round: 1))
            .ToDictionary(group => group.Key, group => group.Sum(run => run.Count));

        return new WorkflowRunFinishedContract(
            byState.GetValueOrDefault(WorkflowRunState.Completed),
            byState.GetValueOrDefault(WorkflowRunState.Declined),
            byState.GetValueOrDefault(WorkflowRunState.TimedOut),
            byState.GetValueOrDefault(WorkflowRunState.Voided),
            byState.GetValueOrDefault(WorkflowRunState.Stopped));
    }

    private static WorkflowRunStepContract ToStep(
        string nodeKey,
        Guid formId,
        bool isStart,
        bool requiresManualReview,
        int depth,
        bool isRemoved,
        IReadOnlyCollection<WorkflowOpenStepFact> open,
        WorkflowRunFinishedContract finished,
        IReadOnlyDictionary<Guid, WorkflowFormHeader> forms)
    {
        var states = open.Select(step => StateOf(step)).ToList();

        return new WorkflowRunStepContract(
            nodeKey,
            formId,
            TitleOf(forms, formId),
            isStart,
            requiresManualReview,
            depth,
            isRemoved,
            states.Count,
            states.Count(state => state == WorkflowRunState.AwaitingReview),
            states.Count(state => state == WorkflowRunState.AwaitingApplicant),
            states.Count(state => state == WorkflowRunState.TimerRunning),
            states.Count(state => state == WorkflowRunState.RevisionRequested),
            finished);
    }
}
