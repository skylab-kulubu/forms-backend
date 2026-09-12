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
    /// kayıt işlemiyle yazar. Form akışa ait değilse cevaba dokunmaz.
    /// </summary>
    Task<ServiceResult<WorkflowStepOutcome>> SubmitAsync(
        Form form,
        FormResponse response,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// İnceleme sonucunu cevaba yazar ve rotayı seçer. İkisi aynı kayıt işleminde
    /// commit edilir; yönlendirme yapılamıyorsa cevap da değişmez.
    /// </summary>
    Task<ServiceResult<WorkflowStepOutcome>> ReviewAsync(
        FormResponse response,
        FormResponseStatus newStatus,
        Guid reviewerId,
        string? note,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cevabın rotası henüz belirlenmemişse true. Arşivleme gibi cevabı dolaylı
    /// olarak reddeden işlemlerin akışı kilitlemesini engellemek için kullanılır.
    /// </summary>
    Task<bool> HasPendingRouteAsync(Guid responseId, CancellationToken cancellationToken = default);
}
