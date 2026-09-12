namespace Skylab.Forms.Application.Contracts.Workflows;

/// <summary>
/// Eski bağlı form istemcisinin beklediği 1..5 aşamasını akış durumundan üretir.
/// Yalnızca iki adımlı (legacy'den dönüştürülmüş) akışlarda anlamlıdır; daha derin
/// akışlarda karşılığı olmadığı için 0 döner.
///
/// Geçici: frontend State ve Stage alanlarına geçtiğinde bu eşleme ve onunla
/// birlikte FormDisplayPayload.Step alanı kaldırılacak.
/// </summary>
public static class LegacyStep
{
    public static int From(WorkflowStepOutcome outcome)
    {
        if (!outcome.IsLegacyTwoStepFlow) return 0;

        return outcome.State switch
        {
            // Reddedilen adım, eski akışta da o adımın başına dönerdi.
            WorkflowActionState.ShowForm or WorkflowActionState.Declined => outcome.Stage == 1 ? 1 : 3,
            WorkflowActionState.AwaitingReview => outcome.Stage == 1 ? 2 : 4,
            WorkflowActionState.Completed => 5,
            _ => 0
        };
    }
}
