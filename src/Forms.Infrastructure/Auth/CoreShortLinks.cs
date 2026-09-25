using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class CoreShortLinks : ICoreShortLinks
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public CoreShortLinks(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<CoreLinkResult> GetAsync(Guid formId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/v1/urls/forms/{formId}", null, ct);

    public Task<CoreLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"/v1/urls/forms/{formId}", new { url, label, alias, actorId }, ct);

    public Task<CoreLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/v1/urls/forms/{formId}", new { alias, suggestion }, ct);

    public async Task<CoreAliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<CoreAliasAvailability>($"/v1/urls/availability?alias={Uri.EscapeDataString(alias)}", Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<CoreLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<CoreLinkStats>($"/v1/urls/forms/{formId}/stats", Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<CoreQrImage?> GetQrAsync(string alias, bool svg, int size, CancellationToken ct = default)
    {
        var query = svg ? "format=svg" : $"size={size}";
        try
        {
            using var response = await _httpClient.GetAsync($"/v1/go/{Uri.EscapeDataString(alias)}/qr?logo=1&utm_source=qr&{query}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? (svg ? "image/svg+xml" : "image/png");
            return new CoreQrImage(content, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<CoreLinkResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var link = await response.Content.ReadFromJsonAsync<CoreLink>(Json, ct);
                return link is null ? new CoreLinkResult(CoreLinkStatus.Failed) : new CoreLinkResult(CoreLinkStatus.Ok, link);
            }

            return new CoreLinkResult(await StatusOfAsync(response, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new CoreLinkResult(CoreLinkStatus.Failed);
        }
    }

    private static async Task<CoreLinkStatus> StatusOfAsync(HttpResponseMessage response, CancellationToken ct)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return CoreLinkStatus.NotFound;
            case HttpStatusCode.Conflict:
                return CoreLinkStatus.Conflict;
            case HttpStatusCode.BadRequest:
                return CoreLinkStatus.Invalid;
            case HttpStatusCode.Forbidden:
                var problem = await ReadProblemAsync(response, ct);
                return problem?.Code == "event_managed" ? CoreLinkStatus.EventManaged : CoreLinkStatus.Failed;
            default:
                return CoreLinkStatus.Failed;
        }
    }

    private static async Task<CoreProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<CoreProblem>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record CoreProblem(string? Code);
}
