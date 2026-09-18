using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class CoreGuestApply : ICoreGuestApply
{
    private readonly HttpClient _httpClient;

    public CoreGuestApply(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<bool> ApplyAsync(Guid eventId, EventGuestIdentity guest, CancellationToken ct = default)
    {
        if (eventId == Guid.Empty || guest is null) return false;
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                $"/v1/events/{eventId}/applications/guest",
                new { firstName = guest.FirstName, lastName = guest.LastName, email = guest.Email },
                ct);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict;
        }
        catch
        {
            return false;
        }
    }
}
