using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Domain.Entities;

public class FormWorkingCopy
{
    public Guid FormId { get; set; }
    public FormDocument Document { get; set; } = null!;
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid UpdatedBy { get; set; }
    public Form Form { get; set; } = null!;

    public static FormWorkingCopy Of(Form form) => new() { FormId = form.Id, Document = FormDocument.Of(form) };

    public static FormWorkingCopy Begin(Guid formId, FormDocument document, Guid userId, DateTime now) =>
        new() { FormId = formId, Document = document, Version = 1, UpdatedAt = now, UpdatedBy = userId };

    public FormEdit Append(FormDocument document, IEnumerable<FormEditOp> applied, Guid batchId, Guid userId, DateTime now)
    {
        Document = document;
        Version += 1;
        UpdatedAt = now;
        UpdatedBy = userId;

        return new FormEdit
        {
            FormId = FormId,
            Version = Version,
            BatchId = batchId,
            UserId = userId,
            Ops = FormEditOp.Serialize(applied),
            CreatedAt = now
        };
    }
}
