using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

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

public record GetQuestionAnswersRequest(
    int Page = 1,
    int PageSize = 8,
    string? Search = null,
    FormResponseStatus? Status = null,
    int? SinceDays = null,
    string? AnswerQuestionId = null,
    string? AnswerValue = null
)
{
    public GetFormAnalyticsRequest Filter => new(Status, SinceDays, AnswerQuestionId, AnswerValue);
}

public record QuestionAnswerContract(
    Guid ResponseId,
    UserContract? User,
    ResponseGuest? Guest,
    DateTime SubmittedAt,
    FormResponseStatus Status,
    string Answer
);
