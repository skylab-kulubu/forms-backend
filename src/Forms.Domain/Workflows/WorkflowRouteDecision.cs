using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Domain.Workflows;

public enum WorkflowRouteOutcome
{
    /// <summary>Tetik için tanımlı rota yok: akış normal şekilde tamamlanır.</summary>
    Terminal = 0,

    /// <summary>Bir rota seçildi.</summary>
    Transition = 1,

    /// <summary>
    /// Aday rota vardı ama hiçbiri tutmadı ve fallback tanımlı değil. Publish
    /// doğrulaması bunu engellediği için yayındaki bir tanımda görülmesi veri
    /// bozulması anlamına gelir.
    /// </summary>
    Unresolved = 2
}

public sealed record WorkflowRouteDecision(WorkflowRouteOutcome Outcome, FormWorkflowTransition? Transition)
{
    public static readonly WorkflowRouteDecision Terminal = new(WorkflowRouteOutcome.Terminal, null);
    public static readonly WorkflowRouteDecision Unresolved = new(WorkflowRouteOutcome.Unresolved, null);

    public static WorkflowRouteDecision To(FormWorkflowTransition transition)
        => new(WorkflowRouteOutcome.Transition, transition);
}
