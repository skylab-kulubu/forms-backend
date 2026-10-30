using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

internal sealed class CoreShortLinkProvider : IShortLinkProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public CoreShortLinkProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ShortLinkResult> GetAsync(Guid formId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, $"/v1/urls/forms/{formId}", null, ct);

    public Task<ShortLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"/v1/urls/forms/{formId}", new { url, label, alias, actorId }, ct);

    public Task<ShortLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, $"/v1/urls/forms/{formId}", new { alias, suggestion }, ct);

    public async Task<AliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default)
    {
        try
        {
            var answer = await _httpClient.GetFromJsonAsync<CoreAvailability>($"/v1/urls/availability?alias={Uri.EscapeDataString(alias)}", Json, ct);
            return answer is null ? null : new AliasAvailability(answer.Alias, answer.Available, answer.Reason);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<ShortLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default)
    {
        try
        {
            var stats = await _httpClient.GetFromJsonAsync<CoreStats>($"/v1/urls/forms/{formId}/stats", Json, ct);
            return stats is null ? null : new ShortLinkStats(stats.Since, stats.Total, [.. (stats.Sources ?? []).Select(source => new ShortLinkSourceCount(source.Source, source.Count, source.Scans))]);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<QrImage?> GetQrAsync(string alias, bool svg, int size, QrTags tags, CancellationToken ct = default)
    {
        var query = new List<string> { "logo=1", svg ? "format=svg" : $"size={size}" };
        foreach (var (key, value) in new[] { ("utm_source", tags.Source), ("utm_medium", tags.Medium), ("utm_campaign", tags.Campaign), ("utm_content", tags.Content) })
        {
            if (!string.IsNullOrEmpty(value)) query.Add($"{key}={Uri.EscapeDataString(value)}");
        }
        try
        {
            using var response = await _httpClient.GetAsync($"/v1/go/{Uri.EscapeDataString(alias)}/qr?{string.Join('&', query)}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var content = await response.Content.ReadAsByteArrayAsync(ct);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? (svg ? "image/svg+xml" : "image/png");
            return new QrImage(content, contentType);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<ShortLinkResult> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body, options: Json);

            using var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var link = await response.Content.ReadFromJsonAsync<CoreLink>(Json, ct);
                return link is null ? new ShortLinkResult(ShortLinkStatus.Failed) : new ShortLinkResult(ShortLinkStatus.Ok, link.ToShortLink());
            }

            return new ShortLinkResult(await StatusOfAsync(response, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new ShortLinkResult(ShortLinkStatus.Failed);
        }
    }

    private static async Task<ShortLinkStatus> StatusOfAsync(HttpResponseMessage response, CancellationToken ct)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.NotFound:
                return ShortLinkStatus.NotFound;
            case HttpStatusCode.Conflict:
                return ShortLinkStatus.Conflict;
            case HttpStatusCode.BadRequest:
                return ShortLinkStatus.Invalid;
            case HttpStatusCode.Forbidden:
                var problem = await ReadProblemAsync(response, ct);
                return problem?.Code == "event_managed" ? ShortLinkStatus.ManagedElsewhere : ShortLinkStatus.Failed;
            default:
                return ShortLinkStatus.Failed;
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

    private sealed record CoreAvailability(string Alias, bool Available, string? Reason);

    private sealed record CoreSourceCount(string Source, int Count, int Scans = 0);

    private sealed record CoreStats(DateTime Since, int Total, List<CoreSourceCount>? Sources);

    private sealed record CoreLink(Guid Id, string Alias, string Url, int ClickCount, Guid? CreatedBy, Guid? FormId, Guid? EventId, string? Label, DateTime CreatedAt)
    {
        public ShortLink ToShortLink() => new(Id, Alias, Url, ClickCount, CreatedBy, Label, EventId is not null, CreatedAt);
    }
}
