using Skylab.Shared.Application.Contracts;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.Metrics;

namespace Skylab.Forms.Application.Services;

public interface IFormMetricService
{
    Task<ServiceResult<FormMetricsContract>> GetFormMetricsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
}