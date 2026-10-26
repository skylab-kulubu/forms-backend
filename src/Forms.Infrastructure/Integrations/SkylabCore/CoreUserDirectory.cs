using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

internal class CoreUserDirectory : IUserDirectory
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ILogger<CoreUserDirectory> _logger;

    public CoreUserDirectory(HttpClient httpClient, ILogger<CoreUserDirectory> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UserContract?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await FindUserAsync(userId, cancellationToken)).User;

    public async Task<UserLookup> FindUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == DeletedUser.Id)
            return new UserLookup(UserLookupOutcome.Found, new UserContract(userId, null, DeletedUser.DisplayName, null));

        try
        {
            var user = await _httpClient.GetFromJsonAsync<ExternalUserResponse>($"/v1/users/{userId}", JsonOptions, cancellationToken);
            if (user is not null && user.Id != Guid.Empty)
                return new UserLookup(UserLookupOutcome.Found, MapToContract(user));

            _logger.LogWarning("Core kullanıcı yanıtı tanınmadı");
            return new UserLookup(UserLookupOutcome.Unavailable);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new UserLookup(UserLookupOutcome.NotFound);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Bulunamayan kullanıcı listede "??" görünür; çağrıyı düşürmek kayıtlı bir değişikliğe 500 döndürürdü.
            _logger.LogWarning("Core kullanıcısı okunamadı: {Error}", ex is HttpRequestException { StatusCode: { } status } ? (int)status : ex.GetType().Name);
            return new UserLookup(UserLookupOutcome.Unavailable);
        }
    }

    public async Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().Where(id => id != Guid.Empty).ToList();
        var users = new UserContract?[ids.Count];

        // Core'da toplu kullanıcı ucu yok; her kullanıcı ayrı sorulur, aynı anda en fazla sekizi.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, ids.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
            async (index, ct) => users[index] = await GetUserAsync(ids[index], ct));

        return [.. users.OfType<UserContract>()];
    }

    /// <summary>
    /// Core silinen ve silinmesi süren kişiyi active dışı bir status ve boş adresle döner; böyle biri
    /// adressiz "Silinmiş kullanıcı" olur, ona posta gitmez.
    /// </summary>
    private static UserContract MapToContract(ExternalUserResponse user)
    {
        if (user.Status is not null && user.Status != "active")
            return new UserContract(user.Id, null, DeletedUser.DisplayName, null);

        return new UserContract(
            user.Id,
            string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.ProfilePictureUrl,
            user.FirstName?.Trim()
        );
    }
}
