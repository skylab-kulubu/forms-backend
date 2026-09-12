using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Workflows;

/// <summary>
/// Bir tetikte izlenecek tek rotayı seçer. Seçim deterministiktir: aynı cevap
/// anlık görüntüsü her zaman aynı transition'ı verir.
/// </summary>
public static class WorkflowTransitionResolver
{
    public static WorkflowRouteDecision Resolve(
        IEnumerable<FormWorkflowTransition> transitions,
        Guid sourceNodeId,
        WorkflowTransitionTrigger trigger,
        WorkflowEvaluationContext context)
    {
        var candidates = transitions
            .Where(transition => transition.SourceNodeId == sourceNodeId && transition.Trigger == trigger)
            .OrderBy(transition => transition.Priority)
            .ThenBy(transition => transition.Id)
            .ToList();

        if (candidates.Count == 0) return WorkflowRouteDecision.Terminal;

        foreach (var candidate in candidates)
        {
            // Varsayılan rota, önceliğinden bağımsız olarak en sona bırakılır.
            if (candidate.IsDefaultRoute) continue;

            if (WorkflowConditionEvaluator.Matches(candidate.Condition, context))
                return WorkflowRouteDecision.To(candidate);
        }

        var defaultRoute = candidates.FirstOrDefault(transition => transition.IsDefaultRoute);

        return defaultRoute is null
            ? WorkflowRouteDecision.Unresolved
            : WorkflowRouteDecision.To(defaultRoute);
    }
}
