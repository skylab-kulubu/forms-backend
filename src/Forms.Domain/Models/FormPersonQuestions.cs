namespace Skylab.Forms.Domain.Models;

public sealed class FormPersonQuestions
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly HashSet<string> _addresses = new(StringComparer.Ordinal);

    public FormPersonQuestions(IEnumerable<FormSchemaItem> schema)
    {
        foreach (var field in schema)
        {
            var inputType = PropText(field, "inputType");
            var identity = PropText(field, "identity");

            if (inputType == "name" || identity is "firstName" or "lastName") _names.Add(field.Id);
            if (inputType == "email" || identity == "email") _addresses.Add(field.Id);
        }
    }

    public string TextIn(IEnumerable<FormResponseSchemaItem> answers) =>
        string.Join(' ', answers
            .Where(answer => _names.Contains(answer.Id) || _addresses.Contains(answer.Id))
            .Select(answer => answer.Answer?.Trim())
            .Where(answer => !string.IsNullOrEmpty(answer)));

    public bool NameIn(IEnumerable<FormResponseSchemaItem> answers) =>
        answers.Any(answer => _names.Contains(answer.Id) && !string.IsNullOrWhiteSpace(answer.Answer));

    private static string? PropText(FormSchemaItem field, string key) =>
        field.Props?.TryGetValue(key, out var value) == true ? value?.ToString() : null;
}
