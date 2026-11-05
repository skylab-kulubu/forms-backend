using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Application.Services.Studio;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Infrastructure.Auth;

namespace Skylab.Forms.Api.Realtime;

public interface IStudioClient
{
    Task Edits(Guid formId, StudioEditContract edit);

    Task Presence(Guid formId, IReadOnlyList<StudioMember> members);

    Task Published(Guid formId, StudioPublishedContract published);

    Task Team(Guid formId, StudioTeamContract team);

    Task Cursor(Guid formId, StudioCursor cursor);
}

public sealed record StudioJoinReply(
    long Version,
    List<StudioEditContract> Edits,
    bool SnapshotRequired,
    bool CanEdit,
    IReadOnlyList<StudioMember> Members
);

[Authorize]
public sealed class StudioHub(IFormStudio studio, StudioPresence presence, ILogger<StudioHub> logger) : Hub<IStudioClient>
{
    public const string Path = "/hubs/studio";

    private const double MaxCursorOffset = 100;

    public static string GroupOf(Guid formId) => $"form:{formId}";

    public async Task<ServiceResult<StudioJoinReply>> Join(Guid formId, long version)
    {
        var caller = Caller();

        var access = await studio.AccessAsync(formId, caller, Context.ConnectionAborted);
        if (access.Data is not { } granted) return Failed<StudioJoinReply>(access.Status, access.Message);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupOf(formId), Context.ConnectionAborted);

        var catchUp = await studio.CatchUpAsync(formId, version, caller, Context.ConnectionAborted);
        if (catchUp.Data is not { } missed)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(formId));
            return Failed<StudioJoinReply>(catchUp.Status, catchUp.Message);
        }

        var members = presence.Join(formId, Context.ConnectionId, caller.UserId, NameOf(Context.User), granted.CanEdit, caller.IsPlatformAdmin);
        await SendAsync(formId, Clients.OthersInGroup(GroupOf(formId)), client => client.Presence(formId, members));

        return new ServiceResult<StudioJoinReply>(
            ServiceStatus.Success,
            Data: new StudioJoinReply(missed.Version, missed.Edits, missed.SnapshotRequired, granted.CanEdit, members));
    }

    public async Task Leave(Guid formId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupOf(formId));

        var members = presence.Leave(formId, Context.ConnectionId);
        await SendAsync(formId, Clients.Group(GroupOf(formId)), client => client.Presence(formId, members));
    }

    public async Task<ServiceResult<StudioEditContract>> Edit(Guid formId, StudioEditRequest request)
    {
        var result = await studio.EditAsync(formId, request, Caller(), Context.ConnectionAborted);

        if (result.Data is { Version: not null } edit)
            await SendAsync(formId, Clients.OthersInGroup(GroupOf(formId)), client => client.Edits(formId, edit));

        return result;
    }

    public async Task Presence(Guid formId, StudioPresenceRequest request)
    {
        if (presence.Update(formId, Context.ConnectionId, request) is { } members)
            await SendAsync(formId, Clients.OthersInGroup(GroupOf(formId)), client => client.Presence(formId, members));
    }

    public async Task Cursor(Guid formId, StudioCursorRequest? request)
    {
        if (presence.UserOf(formId, Context.ConnectionId) is not { } userId) return;

        var cursor = request is { Anchor: { Length: > 0 and <= StudioLimits.MaxKeyLength } anchor } && FitsOffset(request.X) && FitsOffset(request.Y)
            ? new StudioCursor(Context.ConnectionId, userId, anchor, request.X, request.Y)
            : new StudioCursor(Context.ConnectionId, userId, null, 0, 0);

        await SendAsync(formId, Clients.OthersInGroup(GroupOf(formId)), client => client.Cursor(formId, cursor));
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var (formId, members) in presence.Drop(Context.ConnectionId))
            await SendAsync(formId, Clients.Group(GroupOf(formId)), client => client.Presence(formId, members));

        await base.OnDisconnectedAsync(exception);
    }

    private StudioCaller Caller() =>
        new(Guid.Parse(Context.User!.FindFirst("sub")!.Value), FormsPermissions.Has(Context.User, FormsPermissions.PlatformAdmin));

    private async Task SendAsync(Guid formId, IStudioClient clients, Func<IStudioClient, Task> send)
    {
        try
        {
            await send(clients);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Stüdyo olayı {FormId} grubuna gönderilemedi", formId);
        }
    }

    private static string NameOf(ClaimsPrincipal? user)
    {
        var name = user?.FindFirst("name")?.Value;
        if (!string.IsNullOrWhiteSpace(name)) return name;

        var parts = new[] { user?.FindFirst("given_name")?.Value, user?.FindFirst("family_name")?.Value }.Where(part => !string.IsNullOrWhiteSpace(part));
        var full = string.Join(' ', parts);

        return full.Length > 0 ? full : user?.FindFirst("preferred_username")?.Value ?? "Editör";
    }

    private static bool FitsOffset(double value) => double.IsFinite(value) && Math.Abs(value) <= MaxCursorOffset;

    private static ServiceResult<T> Failed<T>(ServiceStatus status, string? message) => new(status, Message: message);
}
