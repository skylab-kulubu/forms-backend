using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.Draft;

public record ResponseDraftContract(
    List<FormResponseSchemaItem> Responses,
    int TimeSpent,
    DateTime? SavedAt,
    List<FormResponseSchemaItem>? Submission = null
);

public record ResponseDraftRequest(
    Guid FormId,
    List<FormResponseSchemaItem> Responses,
    int TimeSpent,
    List<FormResponseSchemaItem>? Submission = null
);
