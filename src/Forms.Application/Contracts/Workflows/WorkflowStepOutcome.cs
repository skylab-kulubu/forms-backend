namespace Skylab.Forms.Application.Contracts.Workflows;

public enum WorkflowActionState
{
    /// <summary>Form yayındaki bir akışın parçası değil; tekil form olarak işlenir.</summary>
    NotInWorkflow = 0,

    ShowForm = 1,
    AwaitingReview = 2,
    Completed = 3,
    Declined = 4,

    /// <summary>Tanım rota üretemedi. Publish doğrulaması bunu engeller.</summary>
    Faulted = 5
}

/// <summary>
/// Bir akış adımının ardından kullanıcıyı bekleyen durum. Görüntüleme, gönderim
/// ve inceleme aynı tipi döndürür ki üç yol da aynı sözleşmeye yansısın.
/// </summary>
/// <param name="Stage">Adımın 1'den başlayan sıra numarası; grafikten değil başvurudan gelir.</param>
/// <param name="FormId">ShowForm durumunda gösterilecek form.</param>
/// <param name="IsLegacyTwoStepFlow">
/// Akış iki adımlı, yani eski bağlı form ikilisinden dönüştürülmüş. Yalnızca eski
/// istemcinin beklediği 1..5 aşamasını hesaplamak için var; frontend State ve
/// Stage alanlarına geçtiğinde kaldırılacak.
/// </param>
public sealed record WorkflowStepOutcome(
    Guid? InstanceId,
    WorkflowActionState State,
    int Stage,
    Guid? FormId,
    string? ReviewNote = null,
    DateTime? ReviewedAt = null,
    bool IsLegacyTwoStepFlow = false)
{
    public static readonly WorkflowStepOutcome NotInWorkflow =
        new(null, WorkflowActionState.NotInWorkflow, 0, null);
}
