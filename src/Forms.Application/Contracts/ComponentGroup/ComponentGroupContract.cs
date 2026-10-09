using Skylab.Forms.Domain.Models;
using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Contracts.ComponentGroup;

public record ComponentGroupContract(
    Guid Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema,
    UserContract? SharedBy = null
);

public record ComponentGroupUpsertRequest(
    Guid? Id,
    string Title,
    string? Description,
    List<FormSchemaItem> Schema
);

public record ShareTokenContract(string Token, DateTime ExpiresAt);

public record ComponentGroupMetaContract(string Title, string? Description, UserContract? SharedBy);
