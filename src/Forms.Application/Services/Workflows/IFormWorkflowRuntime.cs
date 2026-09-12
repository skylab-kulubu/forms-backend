using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Workflows;

/// <summary>
/// Akış yönlendirmesinin tek sahibi. Form ve cevap servisleri rota kararı vermez;
/// buraya devreder. Durumu <see cref="WorkflowActionState.NotInWorkflow"/> dönen
/// bir sonuç, formun tekil form olarak işlenmesi gerektiğini söyler.
/// </summary>
public interface IFormWorkflowRuntime
{
    Task<ServiceResult<WorkflowStepOutcome>> ResolveDisplayAsync(
        Guid formId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Henüz eklenmemiş bir cevabı aktif adıma bağlar, rotayı seçer ve tek bir
    /// kayıt işlemiyle yazar.
    /// </summary>
    Task<ServiceResult<WorkflowStepOutcome>> SubmitAsync(
        Form form,
        FormResponse response,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Onay veya red sonrasında rotayı seçer. Cevabın inceleme alanları çağıran
    /// tarafından yazılır; ikisi de aynı kayıt işleminde commit edilir.
    /// </summary>
    Task<ServiceResult<WorkflowStepOutcome>> ReviewAsync(
        FormResponse response,
        FormResponseStatus newStatus,
        CancellationToken cancellationToken = default);
}
