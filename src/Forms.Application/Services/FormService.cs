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

    public async Task<ServiceResult<FormContract>> UpsertFormAsync(FormUpsertContract contract, Guid userId, CancellationToken cancellationToken = default)
    {
        var formId = contract.Id ?? Guid.NewGuid();

        var existingForm = await _context.Forms.Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == formId, cancellationToken);


        if (contract.AllowAnonymousResponses && !contract.AllowMultipleResponses) return new ServiceResult<FormContract>(FormAccessStatus.NotAcceptable, Message: "Anonim formlarda çoklu yanıt özelliği açık olmalıdır.");

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
            }
            ;

            newForm.Collaborators = collaborators;
            _context.Forms.Add(newForm);

            await _context.SaveChangesAsync(cancellationToken);
            return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(newForm));
        }
        else
        {
            var isAuthorized = existingForm.Collaborators.Any(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor));
            if (!isAuthorized) return new ServiceResult<FormContract>(FormAccessStatus.NotAuthorized, Message: "Bu formu düzenleme yetkiniz yok.");

            var isChildForm = await _context.Forms.AnyAsync(parent => parent.LinkedFormId == formId, cancellationToken);

            existingForm.Title = contract.Title;
            existingForm.Description = contract.Description;
            existingForm.Schema = contract.Schema ?? new();
            existingForm.Status = contract.Status;

            if (!isChildForm)
            {
                existingForm.AllowAnonymousResponses = contract.AllowAnonymousResponses;
                existingForm.AllowMultipleResponses = contract.AllowMultipleResponses;

                if (contract.Collaborators != null)
                {
                    var dbCollaborators = existingForm.Collaborators.ToList();
                    var incomingCollaborators = contract.Collaborators;

                    var toDelete = dbCollaborators
                        .Where(db => db.Role != CollaboratorRole.Owner)
                        .Where(db => !incomingCollaborators.Any(inc => inc.UserId == db.UserId))
                        .ToList();

                    foreach (var item in toDelete) _context.Collaborators.Remove(item);


                    foreach (var incoming in incomingCollaborators)
                    {
                        if (incoming.UserId == userId) continue;

                        var safeRole = incoming.Role == CollaboratorRole.Owner ? CollaboratorRole.Editor : incoming.Role;

                        var existingCollab = dbCollaborators.FirstOrDefault(c => c.UserId == incoming.UserId);
                        if (existingCollab == null)
                        {
                            existingForm.Collaborators.Add(new FormCollaborator { FormId = formId, UserId = incoming.UserId, Role = safeRole });
                        }
                        else if (existingCollab.Role != CollaboratorRole.Owner)
                        {
                            existingCollab.Role = safeRole;
                        }
                    }
                }
                if (existingForm.LinkedFormId.HasValue)
                {
                    var childForm = await _context.Forms.Include(c => c.Collaborators).FirstOrDefaultAsync(c => c.Id == existingForm.LinkedFormId.Value, cancellationToken);

                    if (childForm != null)
                    {
                        childForm.AllowAnonymousResponses = existingForm.AllowAnonymousResponses;
                        childForm.AllowMultipleResponses = existingForm.AllowMultipleResponses;

                        SyncChildCollaborators(childForm, existingForm.Collaborators.ToList());
                    }
                }
            }
            else { }

            await _context.SaveChangesAsync(cancellationToken);
            return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(existingForm));
        }
    }
    public async Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null) return new ServiceResult<FormContract>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = form.Collaborators.Any(c => c.UserId == userId);

        if (!isAuthorized) return new ServiceResult<FormContract>(FormAccessStatus.NotAuthorized, Message: "Yetkiniz yok.");

        return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(form));
    }
    public async Task<ServiceResult<FormDisplayContract>> GetDisplayFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted || form.Status == FormStatus.Closed) return new ServiceResult<FormDisplayContract>(FormAccessStatus.NotFound);
        var parentForm = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.LinkedFormId == id, cancellationToken);

        if (parentForm != null)
        {
            var parentResponse = await _context.Responses.Where(r => r.FormId == parentForm.Id && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);
            if (parentResponse == null || parentResponse.Status != FormResponseStatus.Approved)
            {
                return new ServiceResult<FormDisplayContract>
                (
                    FormAccessStatus.RequiresParentApproval,
                    default,
                    "Bu formu görüntülemek için önceki adımın onaylanması gerekmektedir."
                );
            }
        }

        var latestResponse = await _context.Responses.Where(r => r.FormId == id && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);

        if (latestResponse != null)
        {
            if (latestResponse.Status == FormResponseStatus.Pending)
            {
                return new ServiceResult<FormDisplayContract>(
                    FormAccessStatus.PendingApproval,
                    default,
                    "Form cevabınız inceleniyor, lütfen bekleyiniz."
                );
            }
            if (latestResponse.Status == FormResponseStatus.Approved)
            {
                if (form.LinkedFormId.HasValue) return await GetDisplayFormByIdAsync(form.LinkedFormId.Value, userId, cancellationToken);
                if (!form.AllowMultipleResponses)
                {
                    return new ServiceResult<FormDisplayContract>(
                        FormAccessStatus.Completed,
                        default,
                        "Bu formu daha önce doldurdunuz."
                    );
                }
            }
            if (latestResponse.Status == FormResponseStatus.Declined)
            {
                if (!form.AllowMultipleResponses)
                {
                    return new ServiceResult<FormDisplayContract>(
                        FormAccessStatus.Completed,
                        default,
                        "Başvurunuz reddedilmiştir ve yeni giriş hakkınız yoktur."
                    );
                }
            }
        }
        return new ServiceResult<FormDisplayContract>(
            FormAccessStatus.Available,
            MapToDisplayContract(form)
        );
    }
    public async Task<ServiceResult<List<FormSummaryContract>>> GetUserFormsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var forms = await _context.Forms
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

        return new ServiceResult<List<FormSummaryContract>>(
            FormAccessStatus.Available,
            Data: forms
        );
    }
    public async Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.Where(f => f.Id == id)
            .Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner))
            .FirstOrDefaultAsync(cancellationToken);

        if (form == null) return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "Form bulunamadı veya yetkiniz yok.");

        if (!form.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner)) return new ServiceResult<bool>(FormAccessStatus.NotAuthorized, Message: "Silme yetkiniz yok.");

        var parentForm = await _context.Forms.FirstOrDefaultAsync(f => f.LinkedFormId == id, cancellationToken);

        if (parentForm != null)
        {
            parentForm.LinkedFormId = null;
            parentForm.UpdatedAt = DateTime.UtcNow;
        }

        form.LinkedFormId = null;
        form.Status = FormStatus.Deleted;

        await _context.SaveChangesAsync(cancellationToken);
        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true, Message: "Form silindi.");
    }
    public async Task<ServiceResult<bool>> LinkFormsAsync(Guid parentId, Guid childId, Guid userId, CancellationToken ct = default)
    {
        if (parentId == childId) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Bir form kendisine bağlanamaz.");

        var parentForm = await _context.Forms.Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == parentId, ct);
        var childForm = await _context.Forms.Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == childId, ct);

        if (parentForm == null || childForm == null) return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "Formlardan biri bulunamadı.");

        var isParentAdmin = parentForm.Collaborators.Any(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor));
        var isChildOwner = childForm.Collaborators.Any(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner));

        if (!isParentAdmin || !isChildOwner) return new ServiceResult<bool>(FormAccessStatus.NotAuthorized, Message: "Her iki formda da yönetici yetkisine sahip olmalısınız.");

        if (parentForm.AllowAnonymousResponses) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlar başka bir forma bağlanamaz.");

        if (parentForm.LinkedFormId.HasValue) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Seçilen ana form, halihazırda başka bir forma bağlı olduğu için alt form eklenemez.");

        var isChildAlreadyLinked = await _context.Forms.AnyAsync(f => f.LinkedFormId == childId && f.Id != parentId, ct);
        if (isChildAlreadyLinked) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Seçilen alt form, halihazırda başka bir form tarafından kullanılıyor.");

        if (childForm.LinkedFormId.HasValue) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Seçilen alt formun halihazırda başka bir alt formu var (Zincirleme bağlantı yapılamaz).");

        parentForm.LinkedFormId = childId;

        childForm.AllowAnonymousResponses = parentForm.AllowAnonymousResponses;
        childForm.AllowMultipleResponses = parentForm.AllowMultipleResponses;

        SyncChildCollaborators(childForm, parentForm.Collaborators.ToList());

        await _context.SaveChangesAsync(ct);

        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true, Message: "Formlar başarıyla bağlandı.");
    }
    public async Task<ServiceResult<bool>> UnlinkFormAsync(Guid parentId, Guid userId, CancellationToken ct = default)
    {
        var parentForm = await _context.Forms.Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == parentId, ct);

        if (parentForm == null) return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = parentForm.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner);
        if (!isAuthorized) return new ServiceResult<bool>(FormAccessStatus.NotAuthorized);

        if (!parentForm.LinkedFormId.HasValue) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Bu formun zaten bir bağlantısı yok.");

        parentForm.LinkedFormId = null;

        await _context.SaveChangesAsync(ct);
        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true, Message: "Form bağlantısı kaldırıldı.");
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
    private FormDisplayContract MapToDisplayContract(Form form)
    {
        return new FormDisplayContract(
            form.Id,
            form.Title,
            form.Description,
            form.Schema,
            form.AllowAnonymousResponses,
            form.AllowMultipleResponses,
            form.LinkedFormId.HasValue // HasChildForm
        );
    }
    private void SyncChildCollaborators(Form childForm, List<FormCollaborator> parentCollaborators)
    {
        var childCollabs = childForm.Collaborators.ToList();

        var toDelete = childCollabs.Where(c => !parentCollaborators.Any(p => p.UserId == c.UserId)).ToList();

        foreach (var item in toDelete) childForm.Collaborators.Remove(item);

        foreach (var parentCollab in parentCollaborators)
        {
            var existingChildCollab = childCollabs.FirstOrDefault(c => c.UserId == parentCollab.UserId);

            if (existingChildCollab == null)
            {
                childForm.Collaborators.Add(new FormCollaborator { FormId = childForm.Id, UserId = parentCollab.UserId, Role = parentCollab.Role });
            }
            else if (existingChildCollab.Role != parentCollab.Role)
            {
                existingChildCollab.Role = parentCollab.Role;
            }
        }
    }
}