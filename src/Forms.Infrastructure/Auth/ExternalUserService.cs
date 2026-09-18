using System.Net.Http.Json;
using System.Text.Json;
using Skylab.Forms.Infrastructure.Auth.Contracts;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions;

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

    private static UserContract MapToContract(ExternalUserResponse user)
    {
        return new UserContract(
            user.Id,
            user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.ProfilePictureUrl
        );
    }
}
