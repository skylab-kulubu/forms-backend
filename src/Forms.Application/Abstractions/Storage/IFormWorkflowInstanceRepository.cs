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
    /// okunabilsin diye Workflow da yüklenir.
    /// </summary>
    Task<FormWorkflowInstance?> GetActiveByFormAsync(Guid formId, Guid userId, CancellationToken ct = default);

    /// <summary>Cevabın bağlı olduğu adım; başvurusu ve kardeş adımlarıyla birlikte.</summary>
    Task<FormWorkflowStep?> GetStepByResponseAsync(Guid responseId, CancellationToken ct = default);

    /// <summary>
    /// Kullanıcının bu akıştaki en son başvurusu. Hem "daha önce çalıştırdı mı"
    /// sorusunu hem de sonuçlanmış bir başvurunun kullanıcıya gösterilecek
    /// inceleme notunu karşılar.
    /// </summary>
    Task<WorkflowRunSummary?> GetLastRunAsync(Guid workflowId, Guid userId, CancellationToken ct = default);

    Task<int> CountActiveAsync(Guid workflowId, CancellationToken ct = default);

    /// <summary>Cevap, rotası henüz belirlenmemiş bir adıma mı bağlı?</summary>
    Task<bool> HasOpenStepForResponseAsync(Guid responseId, CancellationToken ct = default);

    /// <summary>Koşul değerlendirmesi için başvurunun cevaplanmış adımları.</summary>
    Task<IReadOnlyList<WorkflowStepAnswers>> GetAnswersAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Cevabın ait olduğu başvurunun bütün adımları. İnceleyen, başvuranın önceki
    /// adımlardaki cevaplarını tek yerden görebilsin diye.
    /// </summary>
    Task<ResponseWorkflowProjection?> GetContextByResponseAsync(Guid responseId, CancellationToken ct = default);

    void Add(FormWorkflowInstance instance);

    /// <summary>
    /// Adımı açıkça ekler. Yalnızca başvurunun koleksiyonuna eklemek yetmez: adımın
    /// anahtarı istemci tarafında dolduğu için EF onu var olan bir satır sanıp
    /// UPDATE üretir.
    /// </summary>
    void Add(FormWorkflowStep step);
}

public sealed record WorkflowStepAnswers(string NodeKey, IReadOnlyList<FormResponseSchemaItem> Answers);

public sealed record ResponseWorkflowProjection(
    Guid InstanceId,
    int Stage,
    IReadOnlyList<ResponseWorkflowStepProjection> Steps);

public sealed record ResponseWorkflowStepProjection(
    int Stage,
    string FormTitle,
    Guid? ResponseId,
    FormResponseStatus? Status);

/// <param name="LastSequence">Başvurunun ulaştığı son adımın sıra numarası.</param>
public sealed record WorkflowRunSummary(
    Guid InstanceId,
    WorkflowInstanceStatus Status,
    WorkflowInstanceOutcome Outcome,
    int LastSequence,
    string? ReviewNote,
    DateTime? ReviewedAt);
