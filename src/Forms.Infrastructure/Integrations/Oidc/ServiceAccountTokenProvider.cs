using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

internal sealed class ServiceAccountTokenProvider : IServiceTokenProvider
{
    public const string HttpClientName = "service-account";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ServiceAccountOptions _options;
    private readonly ILogger<ServiceAccountTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public ServiceAccountTokenProvider(IHttpClientFactory httpClientFactory, IOptions<ServiceAccountOptions> options, ILogger<ServiceAccountTokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (TryGetCached(out var cached)) return cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (TryGetCached(out cached)) return cached;

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope
            };

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(_options.TokenUrl, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Servis hesabı token isteği başarısız: {Status} {Body}", response.StatusCode, body);
                response.EnsureSuccessStatusCode();
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            if (payload is null || string.IsNullOrEmpty(payload.AccessToken))
                throw new InvalidOperationException("Servis hesabı token yanıtı geçersiz (access_token yok).");

            _cachedToken = payload.AccessToken;
            var lifetime = TimeSpan.FromSeconds(Math.Max(payload.ExpiresIn, 5));
            _expiresAt = DateTimeOffset.UtcNow + lifetime - RefreshSkew;

            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryGetCached(out string token)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            token = _cachedToken;
            return true;
        }
        token = string.Empty;
        return false;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("token_type")] string? TokenType
    );
}
