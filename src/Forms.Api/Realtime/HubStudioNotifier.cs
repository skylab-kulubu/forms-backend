using Microsoft.AspNetCore.SignalR;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Api.Realtime;

public sealed class HubStudioNotifier(IHubContext<StudioHub, IStudioClient> hub, StudioPresence presence, ILogger<HubStudioNotifier> logger) : IStudioNotifier
{
    public Task EditsAppliedAsync(Guid formId, StudioEditContract edit, CancellationToken ct = default) =>
        SendAsync(formId, client => client.Edits(formId, edit));

    public Task PublishedAsync(StudioPublishedContract published, CancellationToken ct = default) =>
        SendAsync(published.FormId, client => client.Published(published.FormId, published));

    public Task CommentsChangedAsync(Guid formId, StudioThreadContract thread, CancellationToken ct = default) =>
        SendAsync(formId, client => client.Comments(formId, thread));

    public async Task TeamChangedAsync(StudioTeamContract team, CancellationToken ct = default)
    {
        await SendAsync(team.FormId, client => client.Team(team.FormId, team));

        var roles = team.Collaborators.ToDictionary(collaborator => collaborator.User.Id, collaborator => collaborator.Role);

        foreach (var (connectionId, userId, isPlatformAdmin) in presence.ConnectionsOf(team.FormId))
        {
            var role = roles.GetValueOrDefault(userId, CollaboratorRole.None);

            if (FormAccess.Allows(role, isPlatformAdmin, FormAction.OpenEditor))
            {
                presence.SetCanEdit(team.FormId, connectionId, FormAccess.Allows(role, isPlatformAdmin, FormAction.EditForm));
                continue;
            }

            presence.Leave(team.FormId, connectionId);

            try
            {
                await hub.Groups.RemoveFromGroupAsync(connectionId, StudioHub.GroupOf(team.FormId), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Yetkisi kalkan bağlantı {FormId} grubundan çıkarılamadı", team.FormId);
            }
        }

        await SendAsync(team.FormId, client => client.Presence(team.FormId, presence.MembersOf(team.FormId)));
    }

    private async Task SendAsync(Guid formId, Func<IStudioClient, Task> send)
    {
        try
        {
            await send(hub.Clients.Group(StudioHub.GroupOf(formId)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Stüdyo olayı {FormId} grubuna gönderilemedi", formId);
        }
    }
}
