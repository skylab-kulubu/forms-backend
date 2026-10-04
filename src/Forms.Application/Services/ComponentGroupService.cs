using System.Security.Cryptography;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services;

public class ComponentGroupService : IComponentGroupService
{
    private static readonly TimeSpan ShareTokenLifetime = TimeSpan.FromHours(48);
    private const string TokenKeyPrefix = "componentgroup:share:token:";
    private const string GroupKeyPrefix = "componentgroup:share:group:";

    private readonly IComponentGroupRepository _groups;
    private readonly IFormsUnitOfWork _uow;
    private readonly ICacheService _cache;
    private readonly IExternalUserService _userService;

    public ComponentGroupService(IComponentGroupRepository groups, IFormsUnitOfWork uow, ICacheService cache, IExternalUserService userService)
    {
        _groups = groups;
        _uow = uow;
        _cache = cache;
        _userService = userService;
    }

    public async Task<ServiceResult<ComponentGroupContract>> CreateGroupAsync(ComponentGroupUpsertRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var newGroup = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Description = request.Description,
            Schema = request.Schema ?? new(),
            OwnedBy = userId
        };

        _groups.Add(newGroup);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ServiceResult<ComponentGroupContract>(ServiceStatus.Success, Data: MapToContract(newGroup));
    }

    public async Task<ServiceResult<ComponentGroupContract>> UpdateGroupAsync(Guid id, ComponentGroupUpsertRequest request, Guid userId, CancellationToken cancellationToken = default)
    {
        var existingGroup = await _groups.GetForEditAsync(id, cancellationToken);

        if (existingGroup == null)
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotFound, Message: "Şablon bulunamadı.");

        if (existingGroup.OwnedBy != userId)
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotAuthorized, Message: "Bu şablonu düzenleme yetkiniz yok.");

        existingGroup.Title = request.Title;
        existingGroup.Description = request.Description;
        existingGroup.Schema = request.Schema ?? new();

        await _uow.SaveChangesAsync(cancellationToken);

        return new ServiceResult<ComponentGroupContract>(ServiceStatus.Success, Data: MapToContract(existingGroup));
    }

    public async Task<ServiceResult<PagedResult<ComponentGroupContract>>> GetUserGroupsAsync(Guid userId, GetComponentGroupsRequest request, CancellationToken cancellationToken = default)
    {
        var data = await _groups.GetUserGroupsAsync(userId, request, cancellationToken);
        return new ServiceResult<PagedResult<ComponentGroupContract>>(ServiceStatus.Success, Data: data);
    }

    public async Task<ServiceResult<ComponentGroupContract>> GetGroupByIdAsync(Guid id, Guid userId, string? token, CancellationToken cancellationToken = default)
    {
        var group = await _groups.GetByIdAsync(id, cancellationToken);

        if (group == null)
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotFound, Message: "Şablon bulunamadı.");

        var isOwner = group.OwnedBy == userId;

        if (!isOwner)
        {
            if (string.IsNullOrEmpty(token) || !await IsTokenValidForGroupAsync(token, id, cancellationToken))
                return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");
        }

        var sharedBy = isOwner ? null : await _userService.GetUserAsync(group.OwnedBy, cancellationToken);

        return new ServiceResult<ComponentGroupContract>(ServiceStatus.Success, Data: MapToContract(group, sharedBy));
    }

    public async Task<ServiceResult<bool>> DeleteGroupAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var group = await _groups.GetForLifecycleEditAsync(id, cancellationToken);

        if (group == null || group.OwnedBy != userId)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Şablon bulunamadı veya yetkiniz yok.");

        if (group.ArchivedAt == null)
        {
            await RevokeShareTokenAsync(id, cancellationToken);

            group.ArchivedAt = DateTime.UtcNow;
            group.ArchivedBy = userId;
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Şablon arşivlendi.");
    }

    public async Task<ServiceResult<ComponentGroupContract>> RestoreGroupAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var group = await _groups.GetForLifecycleEditAsync(id, cancellationToken);

        if (group == null || group.OwnedBy != userId)
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotFound, Message: "Şablon bulunamadı veya yetkiniz yok.");

        if (group.ArchivedAt != null)
        {
            group.ArchivedAt = null;
            group.ArchivedBy = null;
            await _uow.SaveChangesAsync(cancellationToken);
        }

        return new ServiceResult<ComponentGroupContract>(
            ServiceStatus.Success,
            Data: MapToContract(group),
            Message: "Şablon geri yüklendi.");
    }

    public async Task<ServiceResult<ShareTokenContract>> CreateOrRefreshShareTokenAsync(Guid groupId, Guid userId, CancellationToken cancellationToken = default)
    {
        var group = await _groups.GetByIdAsync(groupId, cancellationToken);

        if (group == null)
            return new ServiceResult<ShareTokenContract>(ServiceStatus.NotFound, Message: "Şablon bulunamadı.");

        if (group.OwnedBy != userId)
            return new ServiceResult<ShareTokenContract>(ServiceStatus.NotAuthorized, Message: "Bu şablonu paylaşma yetkiniz yok.");

        var existingToken = await _cache.GetAsync<string>(GroupKeyPrefix + groupId, ct: cancellationToken);
        var token = existingToken ?? GenerateToken();

        var expiresAt = DateTime.UtcNow.Add(ShareTokenLifetime);

        await _cache.SetAsync(TokenKeyPrefix + token, groupId, ShareTokenLifetime, cancellationToken);
        await _cache.SetAsync(GroupKeyPrefix + groupId, token, ShareTokenLifetime, cancellationToken);

        return new ServiceResult<ShareTokenContract>(ServiceStatus.Success, Data: new ShareTokenContract(token, expiresAt));
    }

    public async Task<ServiceResult<ComponentGroupMetaContract>> GetGroupMetaAsync(Guid id, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token) || !await IsTokenValidForGroupAsync(token, id, cancellationToken))
            return new ServiceResult<ComponentGroupMetaContract>(ServiceStatus.NotFound);

        var group = await _groups.GetByIdAsync(id, cancellationToken);

        if (group == null)
            return new ServiceResult<ComponentGroupMetaContract>(ServiceStatus.NotFound);

        var sharedBy = await _userService.GetUserAsync(group.OwnedBy, cancellationToken);

        return new ServiceResult<ComponentGroupMetaContract>(
            ServiceStatus.Success,
            Data: new ComponentGroupMetaContract(group.Title, group.Description, sharedBy)
        );
    }

    public async Task<ServiceResult<ComponentGroupContract>> CloneGroupAsync(Guid id, Guid userId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token) || !await IsTokenValidForGroupAsync(token, id, cancellationToken))
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotAuthorized, Message: "Paylaşım bağlantısı geçersiz veya süresi dolmuş.");

        var source = await _groups.GetByIdAsync(id, cancellationToken);

        if (source == null)
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotFound, Message: "Şablon bulunamadı.");

        if (source.OwnedBy == userId)
            return new ServiceResult<ComponentGroupContract>(ServiceStatus.NotAcceptable, Message: "Bu şablon zaten sizin.");

        var clone = new ComponentGroup
        {
            Id = Guid.NewGuid(),
            Title = source.Title,
            Description = source.Description,
            Schema = source.Schema,
            OwnedBy = userId
        };

        _groups.Add(clone);
        await _uow.SaveChangesAsync(cancellationToken);

        return new ServiceResult<ComponentGroupContract>(ServiceStatus.Success, Data: MapToContract(clone));
    }

    private async Task<bool> IsTokenValidForGroupAsync(string token, Guid groupId, CancellationToken cancellationToken)
    {
        var mappedGroupId = await _cache.GetAsync<Guid?>(TokenKeyPrefix + token, ct: cancellationToken);
        return mappedGroupId == groupId;
    }

    private async Task RevokeShareTokenAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var token = await _cache.GetAsync<string>(GroupKeyPrefix + groupId, ct: cancellationToken);
        if (!string.IsNullOrEmpty(token))
            await _cache.RemoveAsync(TokenKeyPrefix + token, cancellationToken);

        await _cache.RemoveAsync(GroupKeyPrefix + groupId, cancellationToken);
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static ComponentGroupContract MapToContract(ComponentGroup group, UserContract? sharedBy = null) =>
        new(group.Id, group.Title, group.Description, group.Schema, sharedBy, group.ArchivedAt, group.ArchivedBy);
}
