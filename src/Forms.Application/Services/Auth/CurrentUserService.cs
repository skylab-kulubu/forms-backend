using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Forms.Application.Contracts;
using Forms.Application.Contracts.Auth;

namespace Forms.Application.Services;

public class RemoteCurrentUserService : ICurrentUserService
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JsonSerializerOptions _jsonOptions;

    public RemoteCurrentUserService(HttpClient httpClient, IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };  
    }

    public async Task<Guid?> GetUserIdAsync(CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader)) return null;

        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);

            var response = await _httpClient.GetFromJsonAsync<ApiResponse<UserContract>>("/internal/api/users/authenticated-user", _jsonOptions, cancellationToken);

            if (response != null && response.Success && response.Data != null) return response.Data.Id;

            return null;
        }
        catch (Exception) { return null; }
    }
}