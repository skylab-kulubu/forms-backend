namespace Skylab.Forms.Domain.Models;

public class FormTask
{
    public string Content { get; set; } = string.Empty;
    public bool Collapsible { get; set; } = true;
    public bool Downloadable { get; set; } = true;
}
