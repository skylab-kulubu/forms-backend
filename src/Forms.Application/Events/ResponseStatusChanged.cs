using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Events;

public sealed record ResponseStatusChanged(Guid ResponseId, Guid FormId, FormResponseStatus Status, Guid? UserId, ResponseGuest? Guest)
{
    public static ResponseStatusChanged Of(FormResponse response) =>
        new(response.Id, response.FormId, response.Status, response.UserId, response.Guest);
}
