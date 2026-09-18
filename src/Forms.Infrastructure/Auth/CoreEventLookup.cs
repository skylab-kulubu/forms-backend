using System.Net.Http.Json;
using System.Text.Json;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Forms;

namespace Skylab.Forms.Infrastructure.Auth;

public sealed class CoreEventLookup : ICoreEventLookup
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<CoreEventResponse>? _events;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public CoreEventLookup(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<EventRefContract?> FindByIdAsync(Guid eventId, CancellationToken ct = default)
    {
        if (eventId == Guid.Empty) return null;
        try
        {
            var row = await _httpClient.GetFromJsonAsync<CoreEventResponse>($"/v1/events/{eventId}", _jsonOptions, ct);
            return Map(row);
        }
        catch
        {
            return null;
        }
    }

    public async Task<EventRefContract?> FindByFormIdAsync(Guid formId, CancellationToken ct = default)
    {
        var map = await FindByFormIdsAsync([formId], ct);
        return map.TryGetValue(formId, out var found) ? found : null;
    }

    public async Task<IReadOnlyDictionary<Guid, EventRefContract>> FindByFormIdsAsync(IEnumerable<Guid> formIds, CancellationToken ct = default)
    {
        var wanted = formIds.Where(id => id != Guid.Empty).ToHashSet();
        if (wanted.Count == 0) return new Dictionary<Guid, EventRefContract>();

        var events = await ListEventsAsync(ct);
        var found = new Dictionary<Guid, EventRefContract>();
        foreach (var row in events)
        {
            foreach (var formId in FormIdsOn(row))
            {
                if (wanted.Contains(formId)) found[formId] = Map(row)!;
            }
        }
        return found;
    }

    private async Task<IReadOnlyList<CoreEventResponse>> ListEventsAsync(CancellationToken ct)
    {
        if (_events is not null && DateTimeOffset.UtcNow < _expiresAt) return _events;
        await _lock.WaitAsync(ct);
        try
        {
            if (_events is not null && DateTimeOffset.UtcNow < _expiresAt) return _events;
            var rows = await _httpClient.GetFromJsonAsync<List<CoreEventResponse>>("/v1/events", _jsonOptions, ct)
                ?? [];
            _events = rows;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(30);
            return _events;
        }
        catch
        {
            return _events ?? [];
        }
        finally
        {
            _lock.Release();
        }
    }

    private static EventRefContract? Map(CoreEventResponse? row)
    {
        if (row is null || row.Id == Guid.Empty) return null;
        return new EventRefContract(row.Id, row.Name);
    }

    private static IEnumerable<Guid> FormIdsOn(CoreEventResponse row)
    {
        foreach (var id in IdsInUrl(row.FormUrl)) yield return id;
        if (row.ExtraFormUrls is null) yield break;
        foreach (var link in row.ExtraFormUrls)
        {
            foreach (var id in IdsInUrl(link.Url)) yield return id;
        }
    }

    private static IEnumerable<Guid> IdsInUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) yield break;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) yield break;
        foreach (var part in parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Guid.TryParse(part, out var id)) yield return id;
        }
    }

    private sealed class CoreEventResponse
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? FormUrl { get; set; }
        public List<CoreEventFormLink>? ExtraFormUrls { get; set; }
    }

    private sealed class CoreEventFormLink
    {
        public string? Url { get; set; }
    }
}
