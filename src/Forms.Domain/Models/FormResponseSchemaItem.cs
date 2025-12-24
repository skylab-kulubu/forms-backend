namespace Forms.Domain.Models;

public class FormResponseSchemaItem
{
    public string Id {get; set;} = string.Empty;
    public string Question {get; set;} = string.Empty;
    public string? Answer { get; set; } = string.Empty;
}