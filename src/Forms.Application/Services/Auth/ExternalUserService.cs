using System.Net.Http.Json;
using System.Text.Json;
using Forms.Domain.Entities;
using Forms.Application.Contracts;
using Forms.Application.Contracts.Auth;
using Forms.Application.Services;

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
            var response = await _httpClient.GetFromJsonAsync<ApiResponse<ExternalUser>>($"/internal/api/users/{userId}", _jsonOptions, cancellationToken);

            if (response != null && response.Success && response.Data != null)
            {
                return MapToContract(response.Data);
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var distinctIds = userIds.Distinct().Where(id => id != Guid.Empty).ToList();
        if (!distinctIds.Any()) return new List<UserContract>();

        var tasks = distinctIds.Select(id => GetUserAsync(id, cancellationToken));
        var results = await Task.WhenAll(tasks);

        return results.Where(u => u != null).ToList()!;
    }

    private static UserContract MapToContract(ExternalUser user)
    {
        return new UserContract(
            user.Id,
            user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.ProfilePictureUrl
        );
    }
}