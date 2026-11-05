using System.Text.Json.Nodes;
using Skylab.Forms.Application.Contracts.Collaborators;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Application.Contracts.Studio;

public record StudioSnapshotContract(
    FormContract Form,
    StudioWorkingCopyContract Working,
    JsonObject Published,
    FormDraftContract? LegacyDraft
);

public record StudioWorkingCopyContract(
    JsonObject Document,
    long Version,
    bool Dirty,
    DateTime? UpdatedAt,
    Guid? UpdatedBy
);

public record StudioStartRequest(
    JsonObject Document
);

public record StudioAccessContract(
    CollaboratorRole Role,
    bool CanEdit
);

public record StudioEditRequest(
    Guid BatchId,
    JsonArray Ops
);

public record StudioEditContract(
    long? Version,
    Guid BatchId,
    Guid UserId,
    JsonArray Ops,
    int Skipped,
    bool Dirty
)
{
    public static StudioEditContract Of(FormEdit edit, bool dirty, int skipped = 0) =>
        new(edit.Version, edit.BatchId, edit.UserId, FormEditOp.Parse(edit.Ops), skipped, dirty);
}

public record StudioCatchUpContract(
    long Version,
    List<StudioEditContract> Edits,
    bool SnapshotRequired
);

public record StudioPublishRequest(
    long WorkingVersion
);

public record StudioPublishedContract(
    Guid FormId,
    int PublishedVersion,
    long WorkingVersion,
    Guid UserId,
    JsonObject Published,
    bool Dirty
);

public record StudioTeamContract(
    Guid FormId,
    List<FormCollaboratorContract> Collaborators
);
