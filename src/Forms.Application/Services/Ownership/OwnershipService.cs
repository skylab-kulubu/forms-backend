using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Ownership;

/// <summary>
/// Form, şablon ve akış sahipliğini devreder. Sahip kendi içeriğini devreder; platform admini yalnız hesabı silinmiş
/// birinden Silinmiş kullanıcıya kalan içeriği devredebilir, etkin bir kişinin içeriğini alamaz.
/// </summary>
public class OwnershipService : IOwnershipService
{
    private readonly IFormRepository _forms;
    private readonly IComponentGroupRepository _groups;
    private readonly IFormWorkflowRepository _workflows;
    private readonly IComponentGroupService _groupService;
    private readonly IExternalUserService _users;
    private readonly ICurrentUserService _currentUser;
    private readonly IFormsUnitOfWork _uow;

    public OwnershipService(
        IFormRepository forms,
        IComponentGroupRepository groups,
        IFormWorkflowRepository workflows,
        IComponentGroupService groupService,
        IExternalUserService users,
        ICurrentUserService currentUser,
        IFormsUnitOfWork uow)
    {
        _forms = forms;
        _groups = groups;
        _workflows = workflows;
        _groupService = groupService;
        _users = users;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<ServiceResult<bool>> TransferFormAsync(Guid formId, Guid targetUserId, Guid userId, CancellationToken ct = default)
    {
        var form = await _forms.GetForEditWithCollaboratorsAsync(formId, ct);
        var ownerId = form?.Collaborators.FirstOrDefault(c => c.Role == CollaboratorRole.Owner)?.UserId;
        if (form is null || ownerId is null || form.Status == FormStatus.Deleted)
            return Fail(ServiceStatus.NotFound, "Form bulunamadı.");

        if (!await CanTransferAsync(ownerId.Value, userId, ct))
            return Fail(ServiceStatus.NotAuthorized, "Bu formu devretme yetkiniz yok.");

        if (await FindTargetProblemAsync(ownerId.Value, targetUserId, ct) is { } problem)
            return problem;

        // Akışın sahibi adım formlarının da sahibi olmak zorunda; akıştaki form tek başına devredilirse akış yeniden
        // doğrulanamaz.
        var memberships = await _workflows.GetFormMembershipsAsync([formId], ct);
        if (memberships.TryGetValue(formId, out var membership))
            return Fail(ServiceStatus.Conflict, $"Bu form \"{membership.WorkflowName}\" akışında; formu akışla birlikte devredin.");

        return await HandOverAsync([form], ownerId.Value, targetUserId, "Formun sahipliği devredildi.", ct);
    }

    public async Task<ServiceResult<bool>> TransferTemplateAsync(Guid groupId, Guid targetUserId, Guid userId, CancellationToken ct = default)
    {
        var group = await _groups.GetForEditAsync(groupId, ct);
        if (group is null) return Fail(ServiceStatus.NotFound, "Şablon bulunamadı.");

        if (!await CanTransferAsync(group.OwnedBy, userId, ct))
            return Fail(ServiceStatus.NotAuthorized, "Bu şablonu devretme yetkiniz yok.");

        if (await FindTargetProblemAsync(group.OwnedBy, targetUserId, ct) is { } problem)
            return problem;

        // Eski sahibin açtığı paylaşım bağlantısı yeni sahibin adıyla görünmesin.
        await _groupService.RevokeShareTokenAsync(groupId, ct);

        group.OwnedBy = targetUserId;
        await _uow.SaveChangesAsync(ct);

        return new ServiceResult<bool>(ServiceStatus.Success, true, "Şablonun sahipliği devredildi.");
    }

    public async Task<ServiceResult<bool>> TransferWorkflowAsync(Guid workflowId, Guid targetUserId, Guid userId, CancellationToken ct = default)
    {
        var workflow = await _workflows.GetForEditAsync(workflowId, ct);
        if (workflow is null) return Fail(ServiceStatus.NotFound, "Akış bulunamadı.");

        var ownerId = workflow.OwnerUserId;
        if (!await CanTransferAsync(ownerId, userId, ct))
            return Fail(ServiceStatus.NotAuthorized, "Bu akışı devretme yetkiniz yok.");

        if (await FindTargetProblemAsync(ownerId, targetUserId, ct) is { } problem)
            return problem;

        // Akışı doğrulayıp yayınlamak için sahibinin adım formlarının da sahibi olması gerekir. Eski sahibin bütün
        // sürümlerdeki adım formları akışla birlikte devredilir, böylece devam eden başvuruların adımları da yeni
        // sahipte kalır.
        var formIds = await _workflows.GetFormIdsAsync(workflowId, ct);
        var forms = await _forms.GetForEditOwnedWithCollaboratorsAsync(formIds, ownerId, ct);

        workflow.OwnerUserId = targetUserId;
        return await HandOverAsync(forms, ownerId, targetUserId, "Akışın sahipliği adım formlarıyla birlikte devredildi.", ct);
    }

    private async Task<ServiceResult<bool>> HandOverAsync(IReadOnlyCollection<Form> forms, Guid ownerId, Guid targetUserId, string message, CancellationToken ct)
    {
        try
        {
            await _uow.ExecuteInTransactionAsync(token => MoveFormOwnershipAsync(forms, ownerId, targetUserId, token), ct);
        }
        catch (StorageConflictException)
        {
            return Fail(ServiceStatus.Conflict, "Sahiplik bu sırada başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin.");
        }

        return new ServiceResult<bool>(ServiceStatus.Success, true, message);
    }

    // Bir formda tek Owner olabildiği için eski sahip önce indirilip kaydedilir, yeni sahip ayrı bir kayıtta
    // yükseltilir. Eski sahip editör olarak kalır; Silinmiş kullanıcının satırı ise tamamen kalkar.
    private async Task<bool> MoveFormOwnershipAsync(IReadOnlyCollection<Form> forms, Guid ownerId, Guid targetUserId, CancellationToken ct)
    {
        foreach (var form in forms)
        {
            if (form.Collaborators.FirstOrDefault(c => c.UserId == ownerId) is not { } owner) continue;

            if (ownerId == DeletedUser.Id) form.Collaborators.Remove(owner);
            else owner.Role = CollaboratorRole.Editor;
        }

        await _uow.SaveChangesAsync(ct);

        foreach (var form in forms)
        {
            var target = form.Collaborators.FirstOrDefault(c => c.UserId == targetUserId);
            if (target is null)
                form.Collaborators.Add(new FormCollaborator { FormId = form.Id, UserId = targetUserId, Role = CollaboratorRole.Owner });
            else
                target.Role = CollaboratorRole.Owner;
        }

        await _uow.SaveChangesAsync(ct);
        return true;
    }

    private async Task<ServiceResult<bool>?> FindTargetProblemAsync(Guid ownerId, Guid targetUserId, CancellationToken ct)
    {
        if (targetUserId == ownerId)
            return Fail(ServiceStatus.NotAcceptable, "Seçilen kişi zaten sahibi.");

        // Silinmiş ya da silinmesi süren kişi core'dan adressiz döner; sahiplik yalnız etkin bir hesaba geçer.
        var target = await _users.GetUserAsync(targetUserId, ct);
        if (string.IsNullOrWhiteSpace(target?.Email))
            return Fail(ServiceStatus.NotAcceptable, "Seçilen kullanıcı bulunamadı.");

        return null;
    }

    private async Task<bool> CanTransferAsync(Guid ownerId, Guid userId, CancellationToken ct) =>
        ownerId == userId || (ownerId == DeletedUser.Id && await _currentUser.HasRoleAsync("skyforms:*", "forms", ct));

    private static ServiceResult<bool> Fail(ServiceStatus status, string message) => new(status, Message: message);
}
