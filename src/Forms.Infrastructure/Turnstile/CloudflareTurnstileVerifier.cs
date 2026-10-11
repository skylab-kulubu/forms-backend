using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Turnstile;

public sealed class CloudflareTurnstileVerifier : ITurnstileVerifier
{
    private const string VerifyPath = "/turnstile/v0/siteverify";
    private const int MaxTokenLength = 2048;
    private const int MaxAttempts = 2;
    private const string InternalError = "internal-error";
    private const string HttpErrorPrefix = "http-";
    private static readonly TimeSpan TotalBudget = TimeSpan.FromSeconds(6);
    private static readonly HashSet<string> SecretErrors = new(StringComparer.Ordinal) { "invalid-input-secret", "missing-input-secret" };

    private readonly HttpClient _httpClient;
    private readonly TurnstileOptions _options;
    private readonly TurnstileReachability _reachability;
    private readonly ILogger<CloudflareTurnstileVerifier> _logger;

    public CloudflareTurnstileVerifier(HttpClient httpClient, TurnstileOptions options, TurnstileReachability reachability, ILogger<CloudflareTurnstileVerifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _reachability = reachability;
        _logger = logger;
    }

    public bool IsEnabled => _options.IsEnabled;

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        if (_options.SecretKey is not { } secret) return false;
        if (_reachability.Current is { } known) return known;

        await _reachability.Probe.WaitAsync(ct);
        try
        {
            if (_reachability.Current is { } settled) return settled;

            var fields = new Dictionary<string, string> { ["secret"] = secret, ["response"] = string.Empty };

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(TotalBudget);

            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var (reply, _) = await SendAsync(fields, ct, budget.Token);
                if (reply is null) continue;

                _reachability.Record(true);
                return true;
            }

            _reachability.Record(false);
            _logger.LogWarning("Cloudflare Turnstile'a ulaşılamıyor; token'sız misafir gönderimleri kesinti sürdükçe kabul ediliyor");
            return false;
        }
        finally
        {
            _reachability.Probe.Release();
        }
    }

    public async Task<TurnstileVerdict> VerifyAsync(string? token, string action, IPAddress? remoteAddress, CancellationToken ct = default)
    {
        if (_options.SecretKey is not { } secret) return TurnstileVerdict.Disabled;
        if (token is { Length: > MaxTokenLength }) return TurnstileVerdict.Rejected;
        if (string.IsNullOrEmpty(token)) return await IsReachableAsync(ct) ? TurnstileVerdict.Rejected : TurnstileVerdict.Unavailable;

        var fields = new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token,
            ["idempotency_key"] = Guid.NewGuid().ToString()
        };

        if (remoteAddress is not null)
            fields["remoteip"] = (remoteAddress.IsIPv4MappedToIPv6 ? remoteAddress.MapToIPv4() : remoteAddress).ToString();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TotalBudget);

        var failure = string.Empty;
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var (reply, error) = await SendAsync(fields, ct, budget.Token);
            if (reply is not null)
            {
                _reachability.Record(true);
                return Judge(reply, action);
            }

            failure = error;
        }

        _reachability.Record(false);
        _logger.LogWarning("Turnstile doğrulaması {Attempts} denemede tamamlanamadı ({Failure})", MaxAttempts, failure);
        return TurnstileVerdict.Unavailable;
    }

    private async Task<(SiteverifyReply? Reply, string Failure)> SendAsync(
        Dictionary<string, string> fields,
        CancellationToken callerToken,
        CancellationToken budgetToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(fields);
            using var response = await _httpClient.PostAsync(VerifyPath, content, budgetToken);

            var code = (int)response.StatusCode;
            if (code >= 500) return (null, $"HTTP {code}");
            if (code >= 400) return (new SiteverifyReply(false, [$"{HttpErrorPrefix}{code}"], null, null), string.Empty);

            var reply = await response.Content.ReadFromJsonAsync<SiteverifyReply>(budgetToken);
            if (reply is null) return (null, "empty response");
            if (!reply.Success && reply.ErrorCodes?.Contains(InternalError) == true) return (null, InternalError);

            return (reply, string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || ex is OperationCanceledException && !callerToken.IsCancellationRequested)
        {
            return (null, ex.GetType().Name);
        }
    }

    private TurnstileVerdict Judge(SiteverifyReply reply, string action)
    {
        if (!reply.Success)
        {
            var codes = string.Join(',', reply.ErrorCodes ?? []);

            if (reply.ErrorCodes?.Any(SecretErrors.Contains) == true)
                _logger.LogError("Turnstile gizli anahtarı kabul edilmedi ({ErrorCodes}); TURNSTILE_SECRET_KEY ayarını kontrol edin", codes);
            else if (reply.ErrorCodes?.Any(error => error.StartsWith(HttpErrorPrefix, StringComparison.Ordinal)) == true)
                _logger.LogWarning("Cloudflare siteverify isteği geri çevirdi ({ErrorCodes}); misafir doğrulamaları reddediliyor", codes);
            else
                _logger.LogInformation("Turnstile doğrulaması geçmedi ({ErrorCodes})", codes);

            return TurnstileVerdict.Rejected;
        }

        if (_options.UsesTestSecret) return TurnstileVerdict.Passed;

        if (!string.Equals(reply.Action, action, StringComparison.Ordinal))
        {
            _logger.LogWarning("Turnstile action eşleşmedi: beklenen {ExpectedAction}, gelen {Action}", action, reply.Action);
            return TurnstileVerdict.Rejected;
        }

        if (_options.AllowedHostnames.Count > 0 && (reply.Hostname is null || !_options.AllowedHostnames.Contains(reply.Hostname)))
        {
            _logger.LogWarning("Turnstile hostname izinli listede değil: {Hostname}", reply.Hostname);
            return TurnstileVerdict.Rejected;
        }

        return TurnstileVerdict.Passed;
    }

    private sealed record SiteverifyReply(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("action")] string? Action
    );
}
