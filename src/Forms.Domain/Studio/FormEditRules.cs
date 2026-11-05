namespace Skylab.Forms.Domain.Studio;

public sealed class FormEditRules
{
    public static readonly FormEditRules None = new(null, []);

    private readonly string? _workflowName;
    private readonly HashSet<string> _locked;

    public FormEditRules(string? workflowName, IEnumerable<string> lockedQuestionIds)
    {
        _workflowName = workflowName;
        _locked = new HashSet<string>(lockedQuestionIds, StringComparer.Ordinal);
    }

    public static bool Concern(IEnumerable<FormEditOp> ops) =>
        ops.Any(op => op.Kind is FormEditKind.Remove or FormEditKind.Replace or FormEditKind.Reset
            || op.Kind == FormEditKind.Set && op.Path is ["schema", _, "type"]);

    public string? ViolationOf(FormDocument document, FormEditOp op)
    {
        if (_locked.Count == 0) return null;

        return op switch
        {
            { Kind: FormEditKind.Remove, Id: { } id } when _locked.Contains(id) => Removed(id),
            { Kind: FormEditKind.Set, Path: ["schema", var id, "type"] } when _locked.Contains(id) && document.TypeOf(id) is { } type && type != (string?)op.Value => Retyped(id),
            { Kind: FormEditKind.Replace, Document: { } next } => ReplacementViolation(document, next),
            _ => null
        };
    }

    private string? ReplacementViolation(FormDocument document, FormDocument next)
    {
        foreach (var id in _locked.Where(document.HasField))
        {
            if (!next.HasField(id)) return Removed(id);
            if (document.TypeOf(id) != next.TypeOf(id)) return Retyped(id);
        }

        return null;
    }

    private string Removed(string id) => $"'{id}' sorusu '{_workflowName}' akışının yönlendirme koşullarında kullanılıyor; formdan çıkarılamaz.";

    private string Retyped(string id) => $"'{id}' sorusu '{_workflowName}' akışının yönlendirme koşullarında kullanılıyor; tipi değiştirilemez.";
}
