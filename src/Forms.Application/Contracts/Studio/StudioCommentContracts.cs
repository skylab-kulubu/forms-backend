using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Contracts.Studio;

public sealed record StudioThreadRequest(string FieldId, string Body);

public sealed record StudioCommentRequest(string Body);

public sealed record StudioCommentContract(
    Guid Id,
    UserContract Author,
    string Body,
    DateTime CreatedAt,
    DateTime? EditedAt
);

public sealed record StudioThreadContract(
    Guid Id,
    string FieldId,
    DateTime? ResolvedAt,
    UserContract? ResolvedBy,
    List<StudioCommentContract> Comments
);
