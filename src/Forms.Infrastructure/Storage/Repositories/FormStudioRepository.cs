using Microsoft.EntityFrameworkCore;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Infrastructure.Storage.Repositories;

public sealed class FormStudioRepository : IFormStudioRepository
{
    private readonly FormsDbContext _context;

    public FormStudioRepository(FormsDbContext context)
    {
        _context = context;
    }

    public Task<FormWorkingCopy?> GetWorkingCopyAsync(Guid formId, CancellationToken ct = default) =>
        _context.WorkingCopies.FirstOrDefaultAsync(c => c.FormId == formId, ct);

    public Task<FormWorkingCopy?> FindWorkingCopyAsync(Guid formId, CancellationToken ct = default) =>
        _context.WorkingCopies.AsNoTracking().FirstOrDefaultAsync(c => c.FormId == formId, ct);

    public Task<FormEdit?> FindEditAsync(Guid formId, Guid batchId, CancellationToken ct = default) =>
        _context.FormEdits.AsNoTracking().FirstOrDefaultAsync(e => e.FormId == formId && e.BatchId == batchId, ct);

    public Task<List<FormEdit>> GetEditsAfterAsync(Guid formId, long version, int take, CancellationToken ct = default) =>
        _context.FormEdits.AsNoTracking()
            .Where(e => e.FormId == formId && e.Version > version)
            .OrderBy(e => e.Version)
            .Take(take)
            .ToListAsync(ct);

    public Task<List<FormComment>> GetCommentsAsync(Guid formId, CancellationToken ct = default) =>
        _context.FormComments.AsNoTracking()
            .Where(c => c.FormId == formId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<List<FormComment>> GetThreadAsync(Guid formId, Guid threadId, CancellationToken ct = default) =>
        _context.FormComments.AsNoTracking()
            .Where(c => c.FormId == formId && (c.Id == threadId || c.ThreadId == threadId))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<FormComment?> GetCommentAsync(Guid formId, Guid commentId, CancellationToken ct = default) =>
        _context.FormComments.FirstOrDefaultAsync(c => c.FormId == formId && c.Id == commentId, ct);

    public void Add(FormWorkingCopy copy) => _context.WorkingCopies.Add(copy);

    public void Add(FormEdit edit) => _context.FormEdits.Add(edit);

    public void Add(FormComment comment) => _context.FormComments.Add(comment);

    public void Remove(FormComment comment) => _context.FormComments.Remove(comment);
}
