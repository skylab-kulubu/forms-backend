using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Domain.Studio;

public sealed record FormEditResult(FormDocument? Document, IReadOnlyList<FormEditOp> Applied, int Skipped, string? Violation)
{
    public static FormEditResult Rejected(string violation) => new(null, [], 0, violation);
}

public static class FormDocumentEditor
{
    public static FormEditResult Apply(FormDocument current, IReadOnlyList<FormEditOp> ops, FormDocument published, FormEditRules rules)
    {
        var next = current.Clone();
        var applied = new List<FormEditOp>(ops.Count);

        foreach (var requested in ops)
        {
            var op = requested.Kind == FormEditKind.Reset ? FormEditOp.ReplaceWith(published) : requested;

            if (rules.ViolationOf(next, op) is { } violation) return FormEditResult.Rejected(violation);

            if (next.TryApply(op)) applied.Add(op);
        }

        if (next.ByteSize > StudioLimits.MaxDocumentBytes)
            return FormEditResult.Rejected($"Form en fazla {StudioLimits.MaxDocumentBytes / 1024} KB olabilir.");

        return new FormEditResult(next, applied, ops.Count - applied.Count, null);
    }
}
