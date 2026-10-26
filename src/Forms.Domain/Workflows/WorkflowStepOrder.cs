using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Domain.Workflows;

public static class WorkflowStepOrder
{
    public static IReadOnlyList<WorkflowOrderedNode> Arrange(
        IReadOnlyList<FormWorkflowNode> nodes,
        IReadOnlyList<FormWorkflowTransition> transitions)
    {
        var depth = new Dictionary<Guid, int>();
        var discovered = new Dictionary<Guid, int>();
        var start = nodes.FirstOrDefault(node => node.IsStart);

        if (start is not null)
        {
            var routes = transitions
                .Where(transition => transition.TargetNodeId is not null)
                .OrderBy(transition => transition.Trigger)
                .ThenBy(transition => transition.IsDefaultRoute ? 1 : 0)
                .ThenBy(transition => transition.Priority)
                .ToLookup(transition => transition.SourceNodeId, transition => transition.TargetNodeId!.Value);

            depth[start.Id] = 1;
            var pending = new Queue<Guid>([start.Id]);
            var guard = nodes.Count * (transitions.Count + 1) + 1;

            while (pending.Count > 0 && guard-- > 0)
            {
                var current = pending.Dequeue();

                foreach (var target in routes[current])
                {
                    if (depth.GetValueOrDefault(target) >= depth[current] + 1) continue;

                    depth[target] = depth[current] + 1;
                    pending.Enqueue(target);
                }
            }

            discovered[start.Id] = 0;
            var unvisited = new Queue<Guid>([start.Id]);

            while (unvisited.Count > 0)
            {
                foreach (var target in routes[unvisited.Dequeue()])
                {
                    if (discovered.TryAdd(target, discovered.Count)) unvisited.Enqueue(target);
                }
            }
        }

        return nodes
            .OrderBy(node => depth.GetValueOrDefault(node.Id, int.MaxValue))
            .ThenBy(node => discovered.GetValueOrDefault(node.Id, int.MaxValue))
            .ThenBy(node => node.NodeKey, StringComparer.Ordinal)
            .Select(node => new WorkflowOrderedNode(node, depth.GetValueOrDefault(node.Id)))
            .ToList();
    }
}

public sealed record WorkflowOrderedNode(FormWorkflowNode Node, int Depth);
