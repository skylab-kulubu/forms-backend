namespace Forms.Domain.Models;

public class FormSchemaItem
{
    public string Id {get; set;} = string.Empty;
    public string Type {get; set;} = string.Empty;
    public Dictionary<string, object?> Props { get; set; } = new();
}