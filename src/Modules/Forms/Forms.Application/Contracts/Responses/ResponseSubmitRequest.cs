using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Responses;

public record ResponseSubmitRequest(
    Guid FormId,
    List<FormResponseSchemaItem> Responses,
    int TimeSpent
);