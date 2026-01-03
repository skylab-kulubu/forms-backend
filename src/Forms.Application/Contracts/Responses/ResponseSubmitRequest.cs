using Forms.Domain.Models;

namespace Forms.Application.Contracts.Responses;

public record ResponseSubmitRequest(
    Guid FormId,
    List<FormResponseSchemaItem> Responses
);