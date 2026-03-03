using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.ComponentGroup;

public record ComponentGroupContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema
);

public record ComponentGroupUpsertRequest(
    Guid? Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema
);