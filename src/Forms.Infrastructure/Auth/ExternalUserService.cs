using System.Net.Http.Json;
using System.Text.Json;
using Skylab.Forms.Infrastructure.Auth.Contracts;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Infrastructure.Auth;

public class ExternalUserService : IExternalUserService
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public ExternalUserService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public async Task<UserContract?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Silinmiş kullanıcının yer tutucusu için core'a sorulmaz.
        if (userId == DeletedUser.Id)
            return DeletedUserContract(userId);

        try
        {
            var user = await _httpClient.GetFromJsonAsync<ExternalUserResponse>($"/v1/users/{userId}", _jsonOptions, cancellationToken);
            if (user is null || user.Id == Guid.Empty)
            {
                return null;
            }
            return MapToContract(user);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var distinctIds = userIds.Distinct().Where(id => id != Guid.Empty).ToList();
        if (!distinctIds.Any()) return [];

        var users = new List<UserContract>(distinctIds.Count);
        foreach (var id in distinctIds)
        {
            var user = await GetUserAsync(id, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }
        return users;
    }

    /// <summary>
    /// Silinen ya da silinmesi süren kişi için core yalnız sabit adı ve boş adres döner
    /// (status deleted|deletion_pending). Etkin olmayan her durum böyle sayılır ve adres
    /// boş tutulur: ona posta gitmez.
    /// </summary>
    private static UserContract DeletedUserContract(Guid userId) =>
        new(userId, null, DeletedUser.DisplayName, null);

    private static UserContract MapToContract(ExternalUserResponse user)
    {
        if (user.Status is not null && user.Status != "active")
            return DeletedUserContract(user.Id);

        return new UserContract(
            user.Id,
            string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.ProfilePictureUrl
        );
    }
}
