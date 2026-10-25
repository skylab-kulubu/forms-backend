using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Events;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Infrastructure.Integrations.SkylabCore;
using Skylab.Forms.Infrastructure.Outbox;

namespace Skylab.Forms.Tests.Infrastructure;

public class CoreResponseFeedTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }

    private static async Task<(bool Done, StubHandler Core)> DeliverAsync(ResponseStatusChanged change, HttpStatusCode status = HttpStatusCode.Accepted)
    {
        var core = new StubHandler(status);
        var feed = new CoreResponseFeed(new HttpClient(core) { BaseAddress = new Uri("http://core.test") }, NullLogger<CoreResponseFeed>.Instance);

        return (await feed.HandleAsync(JsonSerializer.Serialize(change, OutboxMessage.Json), CancellationToken.None), core);
    }

    [Theory]
    [InlineData(FormResponseStatus.NonRestrict, "accepted")]
    [InlineData(FormResponseStatus.Approved, "accepted")]
    [InlineData(FormResponseStatus.Pending, "pending")]
    [InlineData(FormResponseStatus.Flagged, "pending")]
    [InlineData(FormResponseStatus.Declined, "declined")]
    [InlineData(FormResponseStatus.Returned, "declined")]
    public async Task Core_hears_the_status_in_its_own_words(FormResponseStatus status, string word)
    {
        var change = new ResponseStatusChanged(Guid.NewGuid(), Guid.NewGuid(), status, Guid.NewGuid(), null);

        var (done, core) = await DeliverAsync(change);

        using var body = JsonDocument.Parse(core.Body!);
        Assert.True(done);
        Assert.Equal($"/v1/forms/{change.FormId}/responses", core.Path);
        Assert.Equal(word, body.RootElement.GetProperty("status").GetString());
        Assert.Equal(change.ResponseId, body.RootElement.GetProperty("responseId").GetGuid());
    }

    [Fact]
    public async Task A_guest_without_a_name_reaches_core_without_a_guest()
    {
        var guest = new ResponseGuest { Email = "misafir@example.com" };

        var (_, core) = await DeliverAsync(new ResponseStatusChanged(Guid.NewGuid(), Guid.NewGuid(), FormResponseStatus.Pending, null, guest));

        using var body = JsonDocument.Parse(core.Body!);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("guest").ValueKind);
    }

    [Fact]
    public async Task A_deleted_users_response_is_never_reported_again()
    {
        var (done, core) = await DeliverAsync(new ResponseStatusChanged(Guid.NewGuid(), Guid.NewGuid(), FormResponseStatus.Approved, DeletedUser.Id, null));

        Assert.True(done);
        Assert.Null(core.Path);
    }

    [Fact]
    public async Task A_refusal_from_core_is_tried_again() =>
        Assert.False((await DeliverAsync(new ResponseStatusChanged(Guid.NewGuid(), Guid.NewGuid(), FormResponseStatus.Approved, Guid.NewGuid(), null), HttpStatusCode.ServiceUnavailable)).Done);
}
