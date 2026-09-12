using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Abstractions.Storage;

public interface IFormWorkflowInstanceRepository
{
    /// <summary>
    /// Kullanıcının bu formu içeren aktif başvurusu. Arama form üzerinden yapılır
    /// çünkü başvuru, yayındaki tanımdan daha eski bir version'a bağlı olabilir.
    /// Yazma için izlenen (tracked) varlık döner.
    /// </summary>
    Task<FormWorkflowInstance?> GetActiveByFormAsync(Guid formId, Guid userId, CancellationToken ct = default);

    /// <summary>Cevabın bağlı olduğu adım; başvurusu ve kardeş adımlarıyla birlikte.</summary>
    Task<FormWorkflowStep?> GetStepByResponseAsync(Guid responseId, CancellationToken ct = default);

    Task<bool> HasAnyRunAsync(Guid workflowId, Guid userId, CancellationToken ct = default);

    /// <summary>Koşul değerlendirmesi için başvurunun cevaplanmış adımları.</summary>
    Task<IReadOnlyList<WorkflowStepAnswers>> GetAnswersAsync(Guid instanceId, CancellationToken ct = default);

    void Add(FormWorkflowInstance instance);
}

public sealed record WorkflowStepAnswers(string NodeKey, IReadOnlyList<FormResponseSchemaItem> Answers);
