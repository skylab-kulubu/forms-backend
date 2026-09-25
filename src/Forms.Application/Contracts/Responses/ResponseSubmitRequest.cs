using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Responses;

public record ResponseSubmitRequest(
    Guid FormId,
    List<FormResponseSchemaItem> Responses,
    int TimeSpent,
    ResponseAttributionRequest? Attribution = null
);

/// <summary>Form sayfasının adresinde yakaladığı utm_* değerleri, ham haliyle.</summary>
public record ResponseAttributionRequest(
    string? Source,
    string? Medium,
    string? Campaign,
    string? Term,
    string? Content
);
