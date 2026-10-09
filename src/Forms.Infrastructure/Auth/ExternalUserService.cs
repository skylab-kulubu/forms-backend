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
            return user is null || user.Id == Guid.Empty ? null : MapToContract(user);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Bulunamayan kullanıcı listede "??" görünür; çağrıyı düşürmek kayıtlı bir değişikliğe 500 döndürürdü.
            return null;
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

    private static UserContract MapToContract(ExternalUserResponse user)
    {
        return new UserContract(
            user.Id,
            user.Email,
            $"{user.FirstName} {user.LastName}".Trim(),
            user.ProfilePictureUrl,
            user.FirstName?.Trim()
        );
    }
}
