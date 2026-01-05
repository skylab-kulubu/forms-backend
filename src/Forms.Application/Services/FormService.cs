using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Forms.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Forms.Application.Contracts;
using Forms.Application.Contracts.Auth;
using Forms.Application.Contracts.Forms;
using Forms.Application.Contracts.Collaborators;
using Microsoft.Extensions.Configuration;

namespace Forms.Application.Services;

public class FormService : IFormService
{
    private readonly AppDbContext _context;
    private readonly IExternalUserService _userService;

    public FormService(AppDbContext context, IExternalUserService userService)
    {
        _context = context;
        _userService = userService;
    }

    public async Task<ServiceResult<FormContract>> UpsertFormAsync(FormUpsertRequest contract, Guid userId, CancellationToken cancellationToken = default)
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

                var collaboratorIds = newForm.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
                var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

                return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(newForm, users, isChildForm: false, userRole: CollaboratorRole.Owner));
            }
            else
            {
                var currentUserCollaborator = existingForm.Collaborators.FirstOrDefault(c => c.UserId == userId);

                if (currentUserCollaborator == null || (currentUserCollaborator.Role != CollaboratorRole.Owner && currentUserCollaborator.Role != CollaboratorRole.Editor))
                    return new ServiceResult<FormContract>(FormAccessStatus.NotAuthorized, Message: "Bu formu düzenleme yetkiniz yok.");

                var currentUserRole = currentUserCollaborator.Role;

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

                        var toDeleteQuery = dbCollaborators
                            .Where(db => db.Role != CollaboratorRole.Owner)
                            .Where(db => db.UserId != userId)
                            .Where(db => !incomingCollaborators.Any(inc => inc.UserId == db.UserId));

                        if (currentUserRole == CollaboratorRole.Editor)
                            toDeleteQuery = toDeleteQuery.Where(db => db.Role == CollaboratorRole.Viewer);

                        var toDelete = toDeleteQuery.ToList();

                        foreach (var item in toDelete)
                        {
                            _context.Collaborators.Remove(item);
                            existingForm.Collaborators.Remove(item);
                        }

                        foreach (var incoming in incomingCollaborators)
                        {
                            if (incoming.UserId == userId) continue;

                            var safeRole = incoming.Role == CollaboratorRole.Owner ? CollaboratorRole.Editor : incoming.Role;

                            if (currentUserRole == CollaboratorRole.Editor)
                                if (safeRole != CollaboratorRole.Viewer) continue;

                            var existingCollab = dbCollaborators.FirstOrDefault(c => c.UserId == incoming.UserId);
                            if (existingCollab == null)
                            {
                                existingForm.Collaborators.Add(new FormCollaborator { FormId = formId, UserId = incoming.UserId, Role = safeRole });
                            }
                            else
                            {
                                if (existingCollab.Role == CollaboratorRole.Owner) continue;
                                if (currentUserRole == CollaboratorRole.Editor && existingCollab.Role == CollaboratorRole.Editor) continue;

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

                var collaboratorIds = existingForm.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
                var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

                return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(existingForm, users, isChildForm: false, userRole: CollaboratorRole.Owner));
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

        var collaborator = form.Collaborators.FirstOrDefault(c => c.UserId == userId && (c.Role == CollaboratorRole.Owner || c.Role == CollaboratorRole.Editor));

        if (collaborator == null) return new ServiceResult<FormContract>(FormAccessStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        var isChildForm = await _context.Forms.AnyAsync(f => f.LinkedFormId == id, cancellationToken);
        var userRole = collaborator.Role;
        return new ServiceResult<FormContract>(FormAccessStatus.Available, Data: MapToContract(form, users, isChildForm, userRole));
    }
    public async Task<ServiceResult<FormDisplayPayload>> GetDisplayFormByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null || form.Status == FormStatus.Deleted || form.Status == FormStatus.Closed) return new ServiceResult<FormDisplayPayload>(FormAccessStatus.NotFound);

        if (!form.AllowAnonymousResponses && userId == null)
        {
            return new ServiceResult<FormDisplayPayload>(
                FormAccessStatus.Unauthorized,
                Message: "Bu formu görüntülemek için giriş yapmalısınız."
            );
        }

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
    public async Task<ServiceResult<FormInfoContract>> GetFormInfoByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().Include(f => f.Collaborators).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (form == null) return new ServiceResult<FormInfoContract>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var canAccess = form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);

        if (!canAccess) return new ServiceResult<FormInfoContract>(FormAccessStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var counts = await _context.Responses.AsNoTracking()
            .Where(r => r.FormId == id)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Waiting = g.Count(r => r.Status == FormResponseStatus.Pending)
            }).FirstOrDefaultAsync(cancellationToken);

        var responseCount = counts?.Total ?? 0;
        var waitingResponses = counts?.Waiting ?? 0;

        var contract = new FormInfoContract(
            form.Id,
            form.Title,
            form.Status,
            form.UpdatedAt ?? form.CreatedAt,
            responseCount,
            waitingResponses,
            AverageTimeSeconds: null,
            LastSeenUsers: Array.Empty<FormLastSeenUserContract>()
        );

        return new ServiceResult<FormInfoContract>(FormAccessStatus.Available, Data: contract);

    }
    public async Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default)
    {
        var query = _context.Forms.AsNoTracking().Where(f => f.Status != FormStatus.Deleted);

        if (request.Role.HasValue) { query = query.Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == request.Role.Value)); }
        else { query = query.Where(f => f.Collaborators.Any(c => c.UserId == userId)); }

        query = query.Where(f => !_context.Forms.Any(parent => parent.LinkedFormId == f.Id && parent.Status != FormStatus.Deleted));

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(f => EF.Functions.ILike(f.Title, $"%{request.Search.Trim()}%"));

        if (request.AllowAnonymous.HasValue)
            query = query.Where(f => f.AllowAnonymousResponses == request.AllowAnonymous.Value);

        if (request.AllowMultiple.HasValue)
            query = query.Where(f => f.AllowMultipleResponses == request.AllowMultiple.Value);

        if (request.HasLinkedForm.HasValue)
        {
            if (request.HasLinkedForm.Value)
                query = query.Where(f => f.LinkedFormId != null);
            else
                query = query.Where(f => f.LinkedFormId == null);
        }

        if (request.SortDirection?.ToLower() == "ascending") { query = query.OrderBy(f => f.UpdatedAt ?? f.CreatedAt); }
        else { query = query.OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt); }

        var totalCount = await query.CountAsync(cancellationToken);

        var forms = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
        .Select(f => new FormSummaryContract(
            f.Id,
            f.Title,
            f.Status,
            f.LinkedFormId,
            f.Collaborators.FirstOrDefault(c => c.UserId == userId)!.Role,
            f.AllowAnonymousResponses,
            f.AllowMultipleResponses,
            f.UpdatedAt ?? f.CreatedAt,
            f.Responses.Count()
        )).ToListAsync(cancellationToken);

        var resultData = new PagedResult<FormSummaryContract>(
            forms,
            totalCount,
            request.Page,
            request.PageSize
        );

        return new ServiceResult<PagedResult<FormSummaryContract>>(FormAccessStatus.Available, Data: resultData);
    }
    public async Task<ServiceResult<List<LinkableFormsContract>>> GetLinkableFormsAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var currentForm = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (currentForm == null)
            return new ServiceResult<List<LinkableFormsContract>>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");

        var alreadyLinkedFormIds = await _context.Forms.AsNoTracking().Where(f => f.Id != id).Where(f => f.LinkedFormId != null && f.Status != FormStatus.Deleted).Select(f => f.LinkedFormId).ToListAsync(cancellationToken);

        var forms = await _context.Forms.AsNoTracking()
        .Include(f => f.Collaborators)
        .Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner))
        .Where(f => f.Id != id)
        .Where(f => f.Status != FormStatus.Deleted)
        .Where(f => f.LinkedFormId == null)
        .Where(f => !alreadyLinkedFormIds.Contains(f.Id))
        .OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt)
        .Select(f => new LinkableFormsContract(f.Id, f.Title))
        .ToListAsync(cancellationToken);

        return new ServiceResult<List<LinkableFormsContract>>(FormAccessStatus.Available, Data: forms);
    }
    public async Task<ServiceResult<bool>> DeleteFormAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.Where(f => f.Id == id)
            .Where(f => f.Collaborators.Any(c => c.UserId == userId && c.Role == CollaboratorRole.Owner))
            .FirstOrDefaultAsync(cancellationToken);

        if (form == null) return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "Form bulunamadı veya yetkiniz yok.");

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
    private static FormContract MapToContract(Form form, List<UserContract> users, bool isChildForm = false, CollaboratorRole userRole = CollaboratorRole.None)
    {
        var collaboratorContracts = new List<FormCollaboratorContract>();

        if (form.Collaborators != null)
        {
            foreach (var collaborator in form.Collaborators)
            {
                var userDetail = users.FirstOrDefault(u => u.Id == collaborator.UserId) ?? new UserContract(collaborator.UserId, null, "??", null);
                collaboratorContracts.Add(new FormCollaboratorContract(
                    userDetail,
                    collaborator.Role
                ));
            }
        }

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
            userRole,
            collaboratorContracts,
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