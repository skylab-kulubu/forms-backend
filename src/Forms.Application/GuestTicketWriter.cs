using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application;

public static class GuestTicketWriter
{
    public static async Task<ServiceResult<ResponseSubmitResult>?> WriteAsync(
        Form form,
        IReadOnlyList<FormResponseSchemaItem> answers,
        ICoreEventLookup events,
        ICoreGuestApply apply,
        CancellationToken cancellationToken = default)
    {
        var eventId = form.EventId;
        if (eventId is null)
            eventId = (await events.FindByFormIdAsync(form.Id, cancellationToken))?.Id;
        if (eventId is not Guid id) return null;
        form.EventId = id;

        var identity = EventIdentity.Extract(form.Schema, answers);
        if (identity is null)
            return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Ad, soyad ve e-posta zorunludur.");

        if (!await apply.ApplyAsync(id, identity, cancellationToken))
            return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAvailable, Message: "Başvuru kaydedilemedi.");

        return null;
    }
}
