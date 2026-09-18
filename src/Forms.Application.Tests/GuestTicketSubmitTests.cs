using Skylab.Forms.Application;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Xunit;

namespace Forms.Application.Tests;

public class GuestTicketSubmitTests
{
    [Fact]
    public async Task Write_creates_guest_ticket_when_form_event_id_missing_but_event_lists_this_form()
    {
        var formId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var eventId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var schema = EventIdentity.Ensure([]);
        var form = new Form
        {
            Id = formId,
            Title = "GECEKODU",
            Status = FormStatus.Open,
            EventId = null,
            Schema = schema,
            AllowAnonymousResponses = true
        };
        var answers = new List<FormResponseSchemaItem>
        {
            new() { Id = schema[0].Id, Answer = "Yusuf" },
            new() { Id = schema[1].Id, Answer = "Acmaci" },
            new() { Id = schema[2].Id, Answer = "yusuf@example.com" }
        };
        var events = new EventLookup(formId, eventId);
        var apply = new RecordingGuestApply();

        var result = await GuestTicketWriter.WriteAsync(form, answers, events, apply);

        Assert.Null(result);
        Assert.Equal(eventId, form.EventId);
        Assert.Equal(eventId, apply.EventId);
        Assert.Equal("Yusuf", apply.Guest!.FirstName);
        Assert.Equal("Acmaci", apply.Guest.LastName);
        Assert.Equal("yusuf@example.com", apply.Guest.Email);
    }

    [Fact]
    public async Task Write_skips_ticket_when_form_is_not_linked_to_an_event()
    {
        var form = new Form
        {
            Id = Guid.NewGuid(),
            Status = FormStatus.Open,
            EventId = null,
            Schema = EventIdentity.Ensure([]),
            AllowAnonymousResponses = true
        };
        var apply = new RecordingGuestApply();

        var result = await GuestTicketWriter.WriteAsync(form, [], new EventLookup(null, null), apply);

        Assert.Null(result);
        Assert.Null(apply.EventId);
    }

    private sealed class EventLookup(Guid? formId, Guid? eventId) : ICoreEventLookup
    {
        public Task<EventRefContract?> FindByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == eventId ? new EventRefContract(id, "GECEKODU") : null);

        public Task<EventRefContract?> FindByFormIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(formId == id && eventId is Guid ev ? new EventRefContract(ev, "GECEKODU") : null);

        public Task<IReadOnlyDictionary<Guid, EventRefContract>> FindByFormIdsAsync(
            IEnumerable<Guid> formIds,
            CancellationToken ct = default)
        {
            var map = new Dictionary<Guid, EventRefContract>();
            foreach (var id in formIds)
            {
                if (formId == id && eventId is Guid ev) map[id] = new EventRefContract(ev, "GECEKODU");
            }
            return Task.FromResult<IReadOnlyDictionary<Guid, EventRefContract>>(map);
        }
    }

    private sealed class RecordingGuestApply : ICoreGuestApply
    {
        public Guid? EventId { get; private set; }
        public EventGuestIdentity? Guest { get; private set; }

        public Task<bool> ApplyAsync(Guid eventId, EventGuestIdentity guest, CancellationToken ct = default)
        {
            EventId = eventId;
            Guest = guest;
            return Task.FromResult(true);
        }
    }
}
