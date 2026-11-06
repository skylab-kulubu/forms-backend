using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Metrics;

namespace Skylab.Forms.Application.Services;

public interface IFormMetricService
{
    Task<ServiceResult<ServiceMetricsContract>> GetServiceMetricsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormMetricsContract>> GetFormMetricsAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormAnswerAnalyticsContract>> GetAnswerAnalyticsAsync(Guid formId, Guid userId, GetFormAnalyticsRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<QuestionAnswerContract>>> GetQuestionAnswersAsync(Guid formId, string questionId, Guid userId, GetQuestionAnswersRequest request, CancellationToken cancellationToken = default);
}
