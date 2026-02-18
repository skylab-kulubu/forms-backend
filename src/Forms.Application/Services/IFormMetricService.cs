using Forms.Application.Contracts;
using Forms.Application.Contracts.Metrics;

namespace Forms.Application.Services;

public interface IFormMetricService
{
    Task<ServiceResult<FormMetricsContract>> GetFormMetricsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
}