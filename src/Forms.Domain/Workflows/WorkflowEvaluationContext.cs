using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Domain.Workflows;

/// <summary>
/// Bir rota kararı verilirken okunabilecek cevaplar. Koşullar yalnızca bu anlık
/// görüntüye bakar: tanım sonradan değişse bile verilmiş karar yeniden hesaplanmaz.
/// </summary>
public sealed class WorkflowEvaluationContext
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<FormResponseSchemaItem>> _answersByNodeKey;

    public WorkflowEvaluationContext(
        string currentNodeKey,
        IReadOnlyDictionary<string, IReadOnlyList<FormResponseSchemaItem>> answersByNodeKey)
    {
        CurrentNodeKey = currentNodeKey;
        _answersByNodeKey = answersByNodeKey;
    }

    /// <summary>Kararın verildiği adımın node anahtarı.</summary>
    public string CurrentNodeKey { get; }

    /// <summary>Boş nodeKey, kararın verildiği adımı gösterir.</summary>
    public IReadOnlyList<FormResponseSchemaItem> AnswersFor(string? nodeKey)
    {
        var key = string.IsNullOrEmpty(nodeKey) ? CurrentNodeKey : nodeKey;

        return _answersByNodeKey.TryGetValue(key, out var answers)
            ? answers
            : Array.Empty<FormResponseSchemaItem>();
    }
}
