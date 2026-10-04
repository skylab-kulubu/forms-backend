using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Workflows;

public static class WorkflowJourneyBuilder
{
    public static WorkflowJourneyContract Build(
        string title,
        WorkflowDefinition definition,
        IReadOnlyList<WorkflowJourneyStepFact> steps,
        IReadOnlyDictionary<Guid, WorkflowFormHeader> forms,
        Guid? pendingNodeId = null)
    {
        var nodes = definition.Nodes.ToDictionary(node => node.Id);
        var ordered = steps.OrderBy(step => step.Sequence).ToList();
        var route = new List<WorkflowJourneyStepContract>();
        FormWorkflowNode? tail = null;

        foreach (var step in ordered)
        {
            nodes.TryGetValue(step.NodeId, out var node);

            route.Add(new WorkflowJourneyStepContract(
                step.Sequence,
                TitleOf(forms, step.FormId),
                StatusOf(step),
                node?.RequiresManualReview ?? false,
                Certain: true,
                step.SubmittedAt,
                step.ReviewedAt,
                step.ReviewNote));
        }

        if (ordered.Count == 0)
        {
            if (pendingNodeId is { } pendingId && nodes.TryGetValue(pendingId, out var pending))
            {
                route.Add(new WorkflowJourneyStepContract(
                    1,
                    TitleOf(forms, pending.FormId),
                    WorkflowJourneyStatus.Current,
                    pending.RequiresManualReview,
                    Certain: true));

                tail = pending;
            }
        }
        else if (ordered[^1].IsOpen && nodes.TryGetValue(ordered[^1].NodeId, out var open))
        {
            tail = open;
        }

        var maxSteps = route.Count;

        if (tail is not null)
        {
            var stage = route[^1].Stage;
            maxSteps = stage + LongestTail(definition, tail.Id, new Dictionary<Guid, int>(), new HashSet<Guid>());

            foreach (var (node, certain) in Predict(definition, nodes, tail))
            {
                stage++;

                route.Add(new WorkflowJourneyStepContract(
                    stage,
                    node is null ? null : TitleOf(forms, node.FormId),
                    WorkflowJourneyStatus.Upcoming,
                    node?.RequiresManualReview ?? false,
                    certain));
            }
        }

        return new WorkflowJourneyContract(title, Math.Max(maxSteps, route.Count), route);
    }

    private static string StatusOf(WorkflowJourneyStepFact step) => step switch
    {
        { IsOpen: true, ResponseStatus: null } => WorkflowJourneyStatus.Current,
        { IsOpen: true } => WorkflowJourneyStatus.InReview,
        { ResponseStatus: null } => WorkflowJourneyStatus.TimedOut,
        { ResponseStatus: FormResponseStatus.Approved } => WorkflowJourneyStatus.Approved,
        { ResponseStatus: FormResponseStatus.Declined } => WorkflowJourneyStatus.Declined,
        _ => WorkflowJourneyStatus.Submitted
    };

    private static IEnumerable<(FormWorkflowNode? Node, bool Certain)> Predict(
        WorkflowDefinition definition,
        IReadOnlyDictionary<Guid, FormWorkflowNode> nodes,
        FormWorkflowNode start)
    {
        var visited = new HashSet<Guid> { start.Id };
        var current = start;
        var certain = true;

        while (true)
        {
            var trigger = current.RequiresManualReview
                ? WorkflowTransitionTrigger.ResponseApproved
                : WorkflowTransitionTrigger.ResponseSubmitted;

            var candidates = definition.Transitions
                .Where(transition => transition.SourceNodeId == current.Id && transition.Trigger == trigger)
                .ToList();

            var targets = candidates.Select(transition => transition.TargetNodeId).Distinct().ToList();
            var formTargets = targets.Where(target => target.HasValue).Select(target => target!.Value).ToList();

            if (formTargets.Count == 0) yield break;

            certain &= targets.Count == 1;

            var fallback = candidates.FirstOrDefault(transition => transition.IsDefaultRoute)?.TargetNodeId;
            var nextId = fallback ?? (formTargets.Count == 1 ? formTargets[0] : (Guid?)null);

            if (nextId is not { } id || !nodes.TryGetValue(id, out var next) || !visited.Add(id))
            {
                yield return (null, false);
                yield break;
            }

            yield return (next, certain);
            current = next;
        }
    }

    private static int LongestTail(WorkflowDefinition definition, Guid nodeId, Dictionary<Guid, int> memo, HashSet<Guid> path)
    {
        if (memo.TryGetValue(nodeId, out var known)) return known;
        if (!path.Add(nodeId)) return 0;

        var longest = definition.Transitions
            .Where(transition => transition.SourceNodeId == nodeId && transition.TargetNodeId.HasValue)
            .Select(transition => transition.TargetNodeId!.Value)
            .Distinct()
            .Select(target => 1 + LongestTail(definition, target, memo, path))
            .DefaultIfEmpty(0)
            .Max();

        path.Remove(nodeId);
        memo[nodeId] = longest;

        return longest;
    }

    private static string? TitleOf(IReadOnlyDictionary<Guid, WorkflowFormHeader> forms, Guid formId) =>
        forms.TryGetValue(formId, out var header) ? header.Title : null;
}
