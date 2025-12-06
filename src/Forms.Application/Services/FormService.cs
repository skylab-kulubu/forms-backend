using Forms.Application.Contracts;
using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Forms.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Forms.Application.Services;

public class FormService : IFormService
{
    private readonly AppDbContext _context;

    public FormService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<FormContract> UpsertAsync(FormUpsertContract contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var formId = contract.Id ?? Guid.NewGuid();

        var existingForm = await _context.Forms.Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == formId, cancellationToken);

        if (contract.LinkedFormId.HasValue)
        {
            if (contract.LinkedFormId == formId)
            {
                throw new InvalidOperationException("Form can't be linked to itself.");
            }

            var linkedFormExists = await _context.Forms.AsNoTracking().AnyAsync(f => f.Id == contract.LinkedFormId, cancellationToken);

            if (!linkedFormExists)
            {
                throw new KeyNotFoundException("The form to be linked was not found.");
            }
        }

        if (existingForm == null)
        {
            var newForm = new Form
            {
                Id = formId,
                Title = contract.Title,
                Description = contract.Description,
                Schema = contract.Schema ?? new(),
                Status = contract.Status,
                AllowAnonymousResponses = contract.AllowAnonymousResponses,
                AllowMultipleResponses = contract.AllowMultipleResponses,
                LinkedFormId = contract.LinkedFormId,
                CreatedAt = DateTime.UtcNow
            };

            var collaborators = new List<FormCollaborator> { new FormCollaborator { FormId = formId, UserId = userId, Role = CollaboratorRole.Owner } };

            if (contract.Collaborators != null)
            {
                foreach (var incoming in contract.Collaborators)
                {
                    if (incoming.UserId == userId) continue;

                    var safeRole = incoming.Role == CollaboratorRole.Owner ? CollaboratorRole.Editor : incoming.Role;

                    collaborators.Add(new FormCollaborator { FormId = formId, UserId = incoming.UserId, Role = safeRole });
                }
            };

            newForm.Collaborators = collaborators;
            _context.Forms.Add(newForm);

            await _context.SaveChangesAsync(cancellationToken);
            return MapToContract(newForm);
        }
        else
        {
            var isAuthorized = existingForm.Collaborators.Any(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor));

            if (!isAuthorized) throw new UnauthorizedAccessException("Bu formu düzenleme yetkiniz yok.");

            existingForm.Title = contract.Title;
            existingForm.Description = contract.Description;
            existingForm.Schema = contract.Schema ?? new();
            existingForm.Status = contract.Status;
            existingForm.AllowAnonymousResponses = contract.AllowAnonymousResponses;
            existingForm.AllowMultipleResponses = contract.AllowMultipleResponses;
            existingForm.LinkedFormId = contract.LinkedFormId;
            existingForm.UpdatedAt = DateTime.UtcNow;

            if (contract.Collaborators != null)
            {
                var dbCollaborators = existingForm.Collaborators.ToList();
                var incomingCollaborators = contract.Collaborators;

                var toDelete = dbCollaborators
                    .Where(db => db.Role != CollaboratorRole.Owner)
                    .Where(db => !incomingCollaborators.Any(inc => inc.UserId == db.UserId))
                    .ToList();

                foreach (var item in toDelete)
                {
                    _context.Collaborators.Remove(item);
                }

                foreach (var incoming in incomingCollaborators)
                {
                    if (incoming.UserId == userId) continue;

                    var safeRole = incoming.Role == CollaboratorRole.Owner ? CollaboratorRole.Editor : incoming.Role;

                    var existingCollab = dbCollaborators.FirstOrDefault(c => c.UserId == incoming.UserId);

                    if (existingCollab == null) existingForm.Collaborators.Add(new FormCollaborator { FormId = formId, UserId = incoming.UserId, Role = safeRole });
                    else
                    {
                        if (existingCollab.Role != CollaboratorRole.Owner) existingCollab.Role = safeRole;
                    }
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return MapToContract(existingForm);
        }
    }
    public async Task<FormContract?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null) return null;

        var isAuthorized = form.Collaborators.Any(c => c.UserId == userId);

        if (!isAuthorized) throw new UnauthorizedAccessException("Unauthorized access to the form.");

        return MapToContract(form);
    }
    public async Task<List<FormSummaryContract>> GetUserFormsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Forms
            .AsNoTracking()
            .Where(f => f.Collaborators.Any(c => c.UserId == userId))
            .Where(f => f.Status != FormStatus.Deleted)
            .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
            .Select(f => new FormSummaryContract(
                f.Id,
                f.Title,
                f.Description,
                f.Status,
                f.LinkedFormId,
                f.UpdatedAt ?? f.CreatedAt,
                f.Responses.Count()
            ))
            .ToListAsync(cancellationToken);
    }
    public async Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.Where(f => f.Id == id)
            .Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner))
            .FirstOrDefaultAsync(cancellationToken);

        if (form == null) return false;

        form.Status = FormStatus.Deleted;
        form.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
    private static FormContract MapToContract(Form form)
    {
        return new FormContract(
            form.Id,
            form.Title,
            form.Description,
            form.Schema,
            form.Status,
            form.AllowAnonymousResponses,
            form.AllowMultipleResponses,
            form.LinkedFormId,
            form.Collaborators?.Select(c => new FormCollaboratorContract(c.UserId, c.Role)).ToList() ?? new List<FormCollaboratorContract>(),
            form.CreatedAt,
            form.UpdatedAt
        );
    }
}