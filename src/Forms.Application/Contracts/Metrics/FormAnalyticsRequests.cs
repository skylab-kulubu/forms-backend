using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Metrics;

public record GetFormAnalyticsRequest(
    FormResponseStatus? Status = null,
    int? SinceDays = null,
    string? AnswerQuestionId = null,
    string? AnswerValue = null
)
{
    public bool IsFiltered => Status is not null || SinceDays is > 0 || !string.IsNullOrEmpty(AnswerQuestionId);
}
