using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormWorkflowInstanceRepository
{
    /// <summary>
    /// Kullanıcının bu formu içeren aktif başvurusu. Arama form üzerinden yapılır
    /// çünkü başvuru, yayındaki tanımdan daha eski bir version'a bağlı olabilir.
    /// Yazma için izlenen (tracked) varlık döner; akışın güncel kabul durumu
    /// okunabilsin diye Workflow da yüklenir. İki akışta başvuru varsa arşivlenmemiş olanı önce gelir.
    /// </summary>
    Task<FormWorkflowInstance?> GetActiveByFormAsync(Guid formId, Guid userId, CancellationToken ct = default);

    /// <summary>Cevabın bağlı olduğu adım; başvurusu, kardeş adımları ve akışıyla birlikte.</summary>
    Task<FormWorkflowStep?> GetStepByResponseAsync(Guid responseId, CancellationToken ct = default);

    /// <summary>
    /// Akışın, açık adımında cevap olmayan aktif başvurularını sonuçsuz bitirir: formu doldurması beklenen kişi
    /// arşivlenen akışta ilerleyemez. İncelemeyi bekleyen başvurular açık kalır.
    /// </summary>
    Task<int> StopRunsAwaitingApplicantAsync(Guid workflowId, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Akışın bütün aktif başvurularının sürümünü değiştirir ve satırlarını işlem sonuna kadar kilitler: arşivle yarışan
    /// bir karar sonraki adımı açamaz, 409 alır.
    /// </summary>
    Task<int> TouchActiveRunsAsync(Guid workflowId, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının bu akıştaki en son başvurusu. Hem "daha önce çalıştırdı mı"
    /// sorusunu hem de sonuçlanmış bir başvurunun kullanıcıya gösterilecek
    /// inceleme notunu karşılar.
    /// </summary>
    Task<WorkflowRunSummary?> GetLastRunAsync(Guid workflowId, Guid userId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid workflowId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowOpenStepFact>> GetOpenStepsAsync(
        IReadOnlyCollection<Guid> workflowIds,
        DateTime now,
        CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowRunTally>> CountRunsAsync(IReadOnlyCollection<Guid> workflowIds, CancellationToken ct = default);

    Task<PagedResult<WorkflowRunRow>> GetRunsAsync(
        Guid workflowId,
        GetWorkflowRunsRequest request,
        DateTime now,
        CancellationToken ct = default);

    /// <summary>Cevap, rotası henüz belirlenmemiş bir adıma mı bağlı?</summary>
    Task<bool> HasOpenStepForResponseAsync(Guid responseId, CancellationToken ct = default);

    /// <summary>Adım hâlâ açık ve başvurusu sürüyor mu; durdurulan başvurunun süreli denemesi buna bakar.</summary>
    Task<bool> IsStepOpenAsync(Guid stepId, CancellationToken ct = default);

    /// <summary>Koşul değerlendirmesi için başvurunun cevaplanmış adımları.</summary>
    Task<IReadOnlyList<WorkflowStepAnswers>> GetAnswersAsync(Guid instanceId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowJourneyStepFact>> GetJourneyStepsAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Cevabın ait olduğu başvurunun bütün adımları. İnceleyen, başvuranın önceki
    /// adımlardaki cevaplarını tek yerden görebilsin diye.
    /// </summary>
    Task<ResponseWorkflowProjection?> GetContextByResponseAsync(Guid responseId, CancellationToken ct = default);

    Task<ResponseWorkflowProjection?> GetContextByStepAsync(Guid stepId, CancellationToken ct = default);

    Task<FormWorkflowStep?> GetStepForEditAsync(Guid stepId, CancellationToken ct = default);

    /// <summary>
    /// Açık adımdan önce düzeltme istenen gönderim: adayın formu onun cevaplarıyla açılır.
    /// Adım ilk gönderimiyse ya da önceki adım başka bir node'daysa null.
    /// </summary>
    Task<WorkflowRevisionFacts?> GetRevisionAsync(Guid stepId, CancellationToken ct = default);

    /// <summary>Başvurunun durumu ve geçersiz sayılıp sayılamayacağını belirleyen gerçekler.</summary>
    Task<WorkflowRunFacts?> GetRunFactsAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>Kişi başvurunun akışının sahibi mi ve adım formlarında hangi rolleri taşıyor; kararı FormAccess verir.</summary>
    Task<WorkflowRunAccess?> GetRunAccessAsync(Guid instanceId, Guid userId, CancellationToken ct = default);

    /// <summary>Geçersiz saymak için başvuru; adımları ve akışıyla, izlenen olarak.</summary>
    Task<FormWorkflowInstance?> GetForVoidAsync(Guid instanceId, CancellationToken ct = default);

    void Add(FormWorkflowInstance instance);

    /// <summary>
    /// Adımı açıkça ekler. Yalnızca başvurunun koleksiyonuna eklemek yetmez: adımın
    /// anahtarı istemci tarafında dolduğu için EF onu var olan bir satır sanıp
    /// UPDATE üretir.
    /// </summary>
    void Add(FormWorkflowStep step);
}

public sealed record WorkflowStepAnswers(string NodeKey, IReadOnlyList<FormResponseSchemaItem> Answers);

public sealed record WorkflowOpenStepFact(
    Guid WorkflowId,
    string NodeKey,
    Guid FormId,
    bool RequiresManualReview,
    bool HasResponse,
    int Round,
    bool TimerRunning);

public sealed record WorkflowRunTally(
    Guid WorkflowId,
    WorkflowInstanceStatus Status,
    WorkflowInstanceOutcome Outcome,
    int Count);

public sealed record WorkflowRunRow(
    Guid InstanceId,
    Guid UserId,
    WorkflowInstanceStatus Status,
    WorkflowInstanceOutcome Outcome,
    string NodeKey,
    Guid FormId,
    int Sequence,
    int Round,
    bool HasResponse,
    DateTime StartedAt,
    DateTime StepStartedAt,
    DateTime? DeadlineAt,
    DateTime? CompletedAt,
    Guid? LatestResponseId,
    Guid? LatestResponseFormId);

public sealed record WorkflowJourneyStepFact(
    int Sequence,
    Guid NodeId,
    Guid FormId,
    bool IsOpen,
    FormResponseStatus? ResponseStatus,
    DateTime? SubmittedAt,
    DateTime? ReviewedAt,
    string? ReviewNote,
    int Round = 1,
    IReadOnlyList<string>? ReturnedQuestionIds = null);

public sealed record ResponseWorkflowProjection(
    Guid InstanceId,
    int Stage,
    IReadOnlyList<ResponseWorkflowStepProjection> Steps,
    int Round = 1);

public sealed record ResponseWorkflowStepProjection(
    int Stage,
    string FormTitle,
    Guid? ResponseId,
    FormResponseStatus? Status,
    int Round = 1,
    DateTime? SubmittedAt = null,
    DateTime? ReviewedAt = null);

/// <param name="LastSequence">Başvurunun ulaştığı son adımın sıra numarası.</param>
public sealed record WorkflowRunSummary(
    Guid InstanceId,
    Guid WorkflowVersionId,
    WorkflowInstanceStatus Status,
    WorkflowInstanceOutcome Outcome,
    int LastSequence,
    string? ReviewNote,
    DateTime? ReviewedAt,
    DateTime? VoidedAt = null,
    string? VoidNote = null);

/// <param name="Round">Adayın şimdi vereceği gönderimin numarası.</param>
/// <param name="Answers">Düzeltme istenen gönderimin cevapları.</param>
public sealed record WorkflowRevisionFacts(
    int Round,
    string? Note,
    DateTime? ReturnedAt,
    IReadOnlyList<string> QuestionIds,
    List<FormResponseSchemaItem> Answers);

/// <param name="StartFormId">Yayındaki sürümün başlangıç formu; yayında sürüm yoksa başvurunun kendi sürümününki.</param>
/// <param name="HasTimedStep">Başvurunun ya da yayındaki sürümün bir adımı süreli form mu.</param>
public sealed record WorkflowRunFacts(
    Guid InstanceId,
    Guid UserId,
    Guid WorkflowId,
    Guid WorkflowOwnerId,
    string WorkflowName,
    WorkflowStatus WorkflowStatus,
    bool AllowMultipleRuns,
    WorkflowIntake Intake,
    DateTime? IntakeClosesAt,
    WorkflowInstanceStatus Status,
    WorkflowInstanceOutcome Outcome,
    DateTime? CompletedAt,
    DateTime? VoidedAt,
    Guid? VoidedBy,
    string? VoidNote,
    Guid StartFormId,
    bool HasTimedStep);

public sealed record WorkflowRunAccess(bool IsWorkflowOwner, IReadOnlyList<CollaboratorRole> StepFormRoles);
