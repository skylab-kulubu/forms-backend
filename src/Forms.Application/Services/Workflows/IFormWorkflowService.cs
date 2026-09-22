using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Workflows;

namespace Skylab.Forms.Application.Services.Workflows;

public interface IFormWorkflowService
{
    Task<ServiceResult<WorkflowContract>> CreateAsync(
        WorkflowUpsertRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<WorkflowContract>> UpdateAsync(
        Guid workflowId,
        WorkflowUpsertRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<WorkflowContract>> GetAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PagedResult<WorkflowSummaryContract>>> GetOwnedAsync(
        Guid userId,
        GetWorkflowsRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<WorkflowVersionSummaryContract>>> GetVersionsAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Taslağın node ve yönlendirme listesini bütün olarak değiştirir.</summary>
    Task<ServiceResult<WorkflowContract>> UpdateDefinitionAsync(
        Guid workflowId,
        WorkflowDefinitionRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Adım seçicinin kaynağı: uygun olmayan formlar sebebiyle birlikte döner.</summary>
    Task<ServiceResult<List<WorkflowAvailableFormContract>>> GetAvailableFormsAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<WorkflowValidationContract>> ValidateAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Taslağı yayına alır. Doğrulama başarısızsa hiçbir şey değişmez ve bulguların
    /// tamamı döner.
    /// </summary>
    Task<ServiceResult<WorkflowValidationContract>> PublishAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Akışı arşivler: yeni başvuru kabul edilmez, devam edenler kendi version'ında
    /// işlemeye devam eder.
    /// </summary>
    Task<ServiceResult<bool>> ArchiveAsync(
        Guid workflowId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
