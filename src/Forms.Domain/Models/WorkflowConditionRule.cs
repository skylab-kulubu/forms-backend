using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Models;

public class WorkflowConditionRule
{
    /// <summary>
    /// Bos ise kural o anda cevaplanan adima bakar. Doluysa basvurunun NodeKey ile
    /// gosterilen onceki adimindaki cevap okunur.
    /// </summary>
    public string? NodeKey { get; set; }
    public string QuestionId { get; set; } = string.Empty;
    public WorkflowConditionComparison Comparison { get; set; }
    public string? Value { get; set; }
    public List<string>? Values { get; set; }
}
