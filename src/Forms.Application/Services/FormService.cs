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
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
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

                if (contract.LinkedFormId.HasValue)
                {
                    var linkResult = await ApplyLinkInternalAsync(newForm, contract.LinkedFormId.Value, userId, cancellationToken);
                    if (linkResult.Status != FormAccessStatus.Available) return new ServiceResult<FormContract>(linkResult.Status, Message: linkResult.Message);
                }
                ;

                newForm.Collaborators = collaborators;
                _context.Forms.Add(newForm);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
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

                if (existingForm.LinkedFormId != contract.LinkedFormId)
                {
                    if (!contract.LinkedFormId.HasValue && existingForm.LinkedFormId.HasValue)
                    {
                        var unlinkResult = await ApplyUnlinkInternalAsync(existingForm, userId, cancellationToken);
                        if (unlinkResult.Status != FormAccessStatus.Available) return new ServiceResult<FormContract>(unlinkResult.Status, Message: unlinkResult.Message);
                    }
                    else if (contract.LinkedFormId.HasValue)
                    {
                        var linkResult = await ApplyLinkInternalAsync(existingForm, contract.LinkedFormId.Value, userId, cancellationToken);
                        if (linkResult.Status != FormAccessStatus.Available) return new ServiceResult<FormContract>(linkResult.Status, Message: linkResult.Message);
                    }
                }

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
                            childForm.Status = existingForm.Status;
                            childForm.AllowAnonymousResponses = existingForm.AllowAnonymousResponses;
                            childForm.AllowMultipleResponses = existingForm.AllowMultipleResponses;

                            SyncChildCollaborators(childForm, existingForm.Collaborators.ToList());
                        }
                    }
                }
                else { }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(existingForm));
            }
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    public async Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null) return new ServiceResult<FormContract>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = form.Collaborators.Any(c => c.UserId == userId);

        if (!isAuthorized) return new ServiceResult<FormContract>(FormAccessStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var isChildForm = await _context.Forms.AnyAsync(f => f.LinkedFormId == id, cancellationToken);
        return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(form, isChildForm));
    }
    public async Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted || form.Status == FormStatus.Closed) return new ServiceResult<FormDisplayPayload>(FormAccessStatus.NotFound);
        var parentForm = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.LinkedFormId == id, cancellationToken);

        bool isParent = form.LinkedFormId.HasValue;
        bool isChild = parentForm != null;

        if (parentForm != null)
        {
            var parentResponse = await _context.Responses.Where(r => r.FormId == parentForm.Id && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);
            if (parentResponse == null || parentResponse.Status != FormResponseStatus.Approved)
            {
                var lockedStep = ResolveStep(isParent, isChild, isCompleted: false);

                return new ServiceResult<FormDisplayPayload>(
                    FormAccessStatus.RequiresParentApproval,
                    new FormDisplayPayload(null, lockedStep),
                    "Bu formu görüntülemek için önceki adımın onaylanması gerekmektedir."
                );
            }
        }

        var latestResponse = await _context.Responses.Where(r => r.FormId == id && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);

        bool isCompleted = latestResponse?.Status == FormResponseStatus.Approved;
        int step = ResolveStep(isParent, isChild, isCompleted);

        if (latestResponse != null)
        {
            if (latestResponse.Status == FormResponseStatus.Pending)
            {
                return new ServiceResult<FormDisplayPayload>(
                    FormAccessStatus.PendingApproval,
                     new FormDisplayPayload(null, step),
                    "Form cevabınız inceleniyor, lütfen bekleyiniz."
                );
            }
            if (latestResponse.Status == FormResponseStatus.Approved)
            {
                if (form.LinkedFormId.HasValue) return await GetDisplayFormByIdAsync(form.LinkedFormId.Value, userId, cancellationToken);
                if (!form.AllowMultipleResponses)
                {
                    return new ServiceResult<FormDisplayPayload>(
                        FormAccessStatus.Completed,
                        new FormDisplayPayload(null, step),
                        "Bu formu daha önce doldurdunuz."
                    );
                }
            }
            if (latestResponse.Status == FormResponseStatus.Declined)
            {
                if (!form.AllowMultipleResponses)
                {
                    return new ServiceResult<FormDisplayPayload>(
                        FormAccessStatus.Declined,
                        new FormDisplayPayload(null, step),
                        "Başvurunuz reddedilmiştir ve yeni giriş hakkınız yoktur."
                    );
                }
            }
        }

        return new ServiceResult<FormDisplayPayload>(
            FormAccessStatus.Available,
            MapToDisplayPayload(form, step)
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
    public async Task<ServiceResult<List<LinkableFormsContract>>> GetLinkableFormsAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var currentForm = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (currentForm == null) 
            return new ServiceResult<List<LinkableFormsContract>>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var alreadyLinkedFormIds = await _context.Forms.AsNoTracking().Where(f => f.LinkedFormId != null && f.Status != FormStatus.Deleted).Select(f => f.LinkedFormId).ToListAsync(cancellationToken);

        var forms = await _context.Forms.AsNoTracking()
        .Include(f => f.Collaborators)
        .Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner))
        .Where(f => f.Id != id) 
        .Where(f => f.Status != FormStatus.Deleted) 
        .Where(f => f.LinkedFormId == null) 
        .Where(f => !alreadyLinkedFormIds.Contains(f.Id)) 
        .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
        .Select(f => new LinkableFormsContract( f.Id, f.Title))
        .ToListAsync(cancellationToken);

        return new ServiceResult<List<LinkableFormsContract>>(FormAccessStatus.Available, Data: forms);
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
    private async Task<ServiceResult<bool>> ApplyLinkInternalAsync(Form parentForm, Guid childId, Guid userId, CancellationToken ct)
    {
        if (parentForm.Id == childId) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Form kendisine bağlanamaz.");

        var childForm = await _context.Forms.Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == childId, ct);
        if (childForm == null) return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "Bağlanacak alt form bulunamadı.");

        var isChildOwner = childForm.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner);
        if (!isChildOwner) return new ServiceResult<bool>(FormAccessStatus.NotAuthorized, Message: "Alt formda Owner yetkiniz olmalı.");

        if (parentForm.AllowAnonymousResponses) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Anonim formlar bağlanamaz.");

        if (childForm.LinkedFormId.HasValue) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Seçilen form zaten başka bir formun alt formu (Zincirleme yasak).");

        var isChildAlreadyLinked = await _context.Forms.AnyAsync(f => f.LinkedFormId == childId && f.Id != parentForm.Id, ct);
        if (isChildAlreadyLinked) return new ServiceResult<bool>(FormAccessStatus.NotAcceptable, Message: "Bu form zaten başka bir form tarafından kullanılıyor.");

        parentForm.LinkedFormId = childId;

        childForm.AllowAnonymousResponses = parentForm.AllowAnonymousResponses;
        childForm.AllowMultipleResponses = parentForm.AllowMultipleResponses;

        SyncChildCollaborators(childForm, parentForm.Collaborators.ToList());

        return new ServiceResult<bool>(FormAccessStatus.Available, true);
    }
    private async Task<ServiceResult<bool>> ApplyUnlinkInternalAsync(Form parentForm, Guid userId, CancellationToken ct)
    {
        parentForm.LinkedFormId = null;

        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true, Message: "Form bağlantısı kaldırıldı.");
    }
    private static FormContract MapToContract(Form form, bool isChildForm = false)
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
            isChildForm,
            form.Collaborators?.Select(c => new FormCollaboratorContract(c.UserId, c.Role)).ToList() ?? new List<FormCollaboratorContract>(),
            form.CreatedAt,
            form.UpdatedAt
        );
    }
    private FormDisplayPayload MapToDisplayPayload(Form form, int step)
    {
        var contract = new FormDisplayContract(
            form.Id,
            form.Title,
            form.Description,
            form.Schema
        );

        return new FormDisplayPayload(contract, step);
    }
    private static int ResolveStep(bool isParent, bool isChild, bool isCompleted)
    {
        switch (isParent, isChild, isCompleted)
        {
            case (true, false, false):
                return 1;
            case (false, true, false):
                return 2;
            case (false, true, true):
                return 3;
            default:
                return 0;
        }
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