using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Contracts.Responses;

/// <param name="QuestionIds">Yalnız düzeltme isterken: adayın formunda vurgulanacak sorular.</param>
public record ResponseStatusUpdateRequest(
    Guid ResponseId,
    FormResponseStatus NewStatus,
    string? Note,
    List<string>? QuestionIds = null
);
