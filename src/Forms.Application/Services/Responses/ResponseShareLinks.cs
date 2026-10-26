using System.Buffers.Text;
using System.Security.Cryptography;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.ComponentGroup;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.Responses;

/// <summary>Cevabın bir saatlik paylaşım bağlantısı: oluşturma, iptal, paylaşım kartı ve bağlantının bir cevabı açıp açmadığı.</summary>
public interface IResponseShareLinks
{
    Task<ServiceResult<ShareTokenContract>> CreateOrRefreshShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> RevokeShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<ResponseMetaContract>> GetResponseMetaAsync(Guid responseId, string token, CancellationToken cancellationToken = default);

    /// <summary>Bağlantı bu cevabı açıyorsa kimin paylaştığı; geçersiz, süresi dolmuş ya da başka bir cevabınsa null.</summary>
    Task<ShareGrant?> FindGrantAsync(Guid responseId, string token, CancellationToken cancellationToken = default);
}

public sealed record ShareGrant(Guid SharedByUserId);

public sealed class ResponseShareLinks : IResponseShareLinks
{
    private static readonly TimeSpan ShareTokenLifetime = TimeSpan.FromHours(1);
    private const string TokenKeyPrefix = "response:share:token:";
    private const string ResponseKeyPrefix = "response:share:response:";

    private record ShareCacheEntry(Guid ResponseId, List<Guid> InstanceResponseIds, Guid SharedByUserId);

    private readonly IFormResponseRepository _responses;
    private readonly IFormWorkflowInstanceRepository _instances;
    private readonly ICacheService _cache;
    private readonly IUserDirectory _userService;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _time;

    public ResponseShareLinks(
        IFormResponseRepository responses,
        IFormWorkflowInstanceRepository instances,
        ICacheService cache,
        IUserDirectory userService,
        ICurrentUser currentUser,
        TimeProvider time)
    {
        _responses = responses;
        _instances = instances;
        _cache = cache;
        _userService = userService;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<ServiceResult<ShareTokenContract>> CreateOrRefreshShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<ShareTokenContract>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        if (!response.Form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.HandleResponses))
            return new ServiceResult<ShareTokenContract>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı paylaşma yetkiniz yok.");

        var context = await _instances.GetContextByResponseAsync(responseId, cancellationToken);

        var relatedResponseIds = (context?.Steps ?? [])
            .Where(step => step.ResponseId.HasValue && step.ResponseId.Value != responseId)
            .Select(step => step.ResponseId!.Value)
            .ToList();

        var existingToken = await _cache.GetAsync<string>(ResponseKeyPrefix + responseId, ct: cancellationToken);
        var token = existingToken ?? GenerateToken();

        var entry = new ShareCacheEntry(responseId, relatedResponseIds, userId);
        var expiresAt = _time.GetUtcNow().UtcDateTime.Add(ShareTokenLifetime);

        await _cache.SetAsync(TokenKeyPrefix + token, entry, ShareTokenLifetime, cancellationToken);
        await _cache.SetAsync(ResponseKeyPrefix + responseId, token, ShareTokenLifetime, cancellationToken);

        foreach (var relatedId in relatedResponseIds)
            await _cache.SetAsync(ResponseKeyPrefix + relatedId, token, ShareTokenLifetime, cancellationToken);

        return new ServiceResult<ShareTokenContract>(ServiceStatus.Success, Data: new ShareTokenContract(token, expiresAt));
    }

    public async Task<ServiceResult<bool>> RevokeShareTokenAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        if (!response.Form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.HandleResponses))
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtın paylaşımını iptal etme yetkiniz yok.");

        var token = await _cache.GetAsync<string>(ResponseKeyPrefix + responseId, ct: cancellationToken);
        if (string.IsNullOrEmpty(token))
            return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Aktif paylaşım yok.");

        var entry = await _cache.GetAsync<ShareCacheEntry>(TokenKeyPrefix + token, ct: cancellationToken);

        await _cache.RemoveAsync(TokenKeyPrefix + token, cancellationToken);
        await _cache.RemoveAsync(ResponseKeyPrefix + (entry?.ResponseId ?? responseId), cancellationToken);

        foreach (var relatedId in entry?.InstanceResponseIds ?? [])
            await _cache.RemoveAsync(ResponseKeyPrefix + relatedId, cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Paylaşım iptal edildi.");
    }

    public async Task<ServiceResult<ResponseMetaContract>> GetResponseMetaAsync(Guid responseId, string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(token))
            return new ServiceResult<ResponseMetaContract>(ServiceStatus.NotFound);

        var grant = await FindGrantAsync(responseId, token, cancellationToken);
        if (grant == null)
            return new ServiceResult<ResponseMetaContract>(ServiceStatus.NotFound);

        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, cancellationToken);
        if (response == null)
            return new ServiceResult<ResponseMetaContract>(ServiceStatus.NotFound);

        var sharedBy = await _userService.GetUserAsync(grant.SharedByUserId, cancellationToken);

        return new ServiceResult<ResponseMetaContract>(
            ServiceStatus.Success,
            Data: new ResponseMetaContract(response.Form.Title, PublicUserContract.From(sharedBy))
        );
    }

    public async Task<ShareGrant?> FindGrantAsync(Guid responseId, string token, CancellationToken cancellationToken = default)
    {
        var entry = await _cache.GetAsync<ShareCacheEntry>(TokenKeyPrefix + token, ct: cancellationToken);

        // Akıştaki bir cevabın bağlantısı başvurunun diğer adımlarını da açar.
        return entry is not null && (entry.ResponseId == responseId || entry.InstanceResponseIds.Contains(responseId))
            ? new ShareGrant(entry.SharedByUserId)
            : null;
    }

    private static string GenerateToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
