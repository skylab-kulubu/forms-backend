using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Api.Realtime;

public sealed record StudioMember(string Key, Guid UserId, string Name, bool CanEdit, IReadOnlyList<string> Selection, string? Editing);

public sealed record StudioPresenceRequest(IReadOnlyList<string>? Selection, string? Editing);

public sealed class StudioPresence
{
    private const int MaxSelection = 50;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Dictionary<string, Entry>> _forms = new();

    private sealed record Entry(StudioMember Member, bool IsPlatformAdmin);

    public IReadOnlyList<StudioMember> Join(Guid formId, string connectionId, Guid userId, string name, bool canEdit, bool isPlatformAdmin)
    {
        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var members)) _forms[formId] = members = new Dictionary<string, Entry>();

            var previous = members.GetValueOrDefault(connectionId)?.Member;
            members[connectionId] = new Entry(new StudioMember(connectionId, userId, name, canEdit, previous?.Selection ?? [], previous?.Editing), isPlatformAdmin);

            return Snapshot(members);
        }
    }

    public IReadOnlyList<StudioMember>? Update(Guid formId, string connectionId, StudioPresenceRequest request)
    {
        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var members) || !members.TryGetValue(connectionId, out var entry)) return null;

            var selection = (request.Selection ?? []).Where(FitsKey).Distinct(StringComparer.Ordinal).Take(MaxSelection).ToArray();
            var editing = request.Editing is { Length: <= StudioLimits.MaxKeyLength * 2 } text ? text : null;

            members[connectionId] = entry with { Member = entry.Member with { Selection = selection, Editing = editing } };

            return Snapshot(members);
        }
    }

    public IReadOnlyList<StudioMember> SetCanEdit(Guid formId, string connectionId, bool canEdit)
    {
        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var members)) return [];

            if (members.TryGetValue(connectionId, out var entry) && entry.Member.CanEdit != canEdit)
                members[connectionId] = entry with { Member = entry.Member with { CanEdit = canEdit } };

            return Snapshot(members);
        }
    }

    public IReadOnlyList<StudioMember> Leave(Guid formId, string connectionId)
    {
        lock (_gate)
        {
            if (!_forms.TryGetValue(formId, out var members)) return [];

            members.Remove(connectionId);
            if (members.Count == 0) _forms.Remove(formId);

            return Snapshot(members);
        }
    }

    public IReadOnlyList<(Guid FormId, IReadOnlyList<StudioMember> Members)> Drop(string connectionId)
    {
        lock (_gate)
        {
            var left = new List<(Guid, IReadOnlyList<StudioMember>)>();

            foreach (var (formId, members) in _forms.ToList())
            {
                if (!members.Remove(connectionId)) continue;

                if (members.Count == 0) _forms.Remove(formId);
                left.Add((formId, Snapshot(members)));
            }

            return left;
        }
    }

    public IReadOnlyList<StudioMember> MembersOf(Guid formId)
    {
        lock (_gate)
        {
            return _forms.TryGetValue(formId, out var members) ? Snapshot(members) : [];
        }
    }

    public IReadOnlyList<(string ConnectionId, Guid UserId, bool IsPlatformAdmin)> ConnectionsOf(Guid formId)
    {
        lock (_gate)
        {
            return _forms.TryGetValue(formId, out var members)
                ? [.. members.Select(pair => (pair.Key, pair.Value.Member.UserId, pair.Value.IsPlatformAdmin))]
                : [];
        }
    }

    private static bool FitsKey(string? key) => key is { Length: > 0 and <= StudioLimits.MaxKeyLength };

    private static IReadOnlyList<StudioMember> Snapshot(Dictionary<string, Entry> members) => [.. members.Values.Select(entry => entry.Member)];
}
