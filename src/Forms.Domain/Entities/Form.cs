using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Domain.Entities;

public class Form : BaseEntity
{
    public string Title { get; set; } = "Yeni Form";
    public string? Description { get; set; }
    public List<FormSchemaItem> Schema { get; set; } = new();
    public FormStatus Status { get; set; } = FormStatus.Open;
    public bool AllowAnonymousResponses { get; set; } = false;
    public bool AllowMultipleResponses { get; set; } = false;
    public bool RequiresManualReview { get; set; } = false;
    /// <summary>
    /// Eski bagli form akisindan kalan alan. Artik hicbir yerde okunmuyor; akislara
    /// donusum sonrasi bir surumluk geri donus penceresi icin kolonla birlikte
    /// duruyor ve bir sonraki surumde kaldirilacak.
    /// </summary>
    public Guid? LinkedFormId { get; set; }
    public Form? LinkedForm { get; set; }

    // Navigation
    public ICollection<FormCollaborator> Collaborators { get; set; } = new List<FormCollaborator>();
    public ICollection<FormResponse> Responses { get; set; } = new List<FormResponse>();

    public void UpdateCollaborators(IEnumerable<(Guid UserId, CollaboratorRole Role)> incomingCollaborators, (Guid UserId, CollaboratorRole Role) currentUser)
    {
        if (incomingCollaborators == null) return;

        var incomingList = incomingCollaborators.ToList();
        var dbCollaborators = this.Collaborators.ToList();

        var toDelete = dbCollaborators
            .Where(db => db.Role != CollaboratorRole.Owner)
            .Where(db => db.UserId != currentUser.UserId)
            .Where(db => !incomingList.Any(inc => inc.UserId == db.UserId))
            .ToList();

        if (currentUser.Role == CollaboratorRole.Editor)
            toDelete = toDelete.Where(db => db.Role == CollaboratorRole.Viewer).ToList();

        foreach (var item in toDelete)
        {
            this.Collaborators.Remove(item);
        }

        foreach (var incoming in incomingList)
        {
            if (incoming.UserId == currentUser.UserId) continue;

            var safeRole = incoming.Role == CollaboratorRole.Owner ? CollaboratorRole.Editor : incoming.Role;

            if (currentUser.Role == CollaboratorRole.Editor && safeRole != CollaboratorRole.Viewer) safeRole = CollaboratorRole.Viewer;

            var existing = dbCollaborators.FirstOrDefault(c => c.UserId == incoming.UserId);

            if (existing == null)
            {
                this.Collaborators.Add(new FormCollaborator
                {
                    FormId = this.Id,
                    UserId = incoming.UserId,
                    Role = safeRole
                });
            }
            else
            {
                if (existing.Role == CollaboratorRole.Owner) continue;

                if (currentUser.Role == CollaboratorRole.Editor && existing.Role == CollaboratorRole.Editor) continue;

                if (existing.Role != safeRole)
                    existing.Role = safeRole;
            }
        }
    }
}
