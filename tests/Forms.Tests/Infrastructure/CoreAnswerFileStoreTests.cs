using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Auth;

namespace Skylab.Forms.Tests.Infrastructure;

public class CoreAnswerFileStoreTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (CoreAnswerFileStore Store, StubHandler Handler) Store(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);

        return (new CoreAnswerFileStore(new HttpClient(handler) { BaseAddress = new Uri("http://core.test") }, NullLogger<CoreAnswerFileStore>.Instance), handler);
    }

    [Theory]
    [InlineData("answer_file", "scanning", FileOrigin.SignedInAnswer, FileState.Scanning)]
    [InlineData("answer_file_guest", "pending", FileOrigin.GuestAnswer, FileState.Unattached)]
    [InlineData("legacy", "attached", FileOrigin.Legacy, FileState.Attached)]
    [InlineData("avatar", "quarantined", FileOrigin.Unknown, FileState.Unknown)]
    public async Task Cores_purpose_and_status_become_forms_origin_and_state(string purpose, string status, FileOrigin origin, FileState state)
    {
        var id = Guid.NewGuid();
        var (store, _) = Store(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, name = "cv.pdf", type = "application/pdf", size = 10, purpose, status, visibility = "private" }));

        var file = (await store.FindAsync(id)).File;

        Assert.Equal((origin, state, true), (file?.Origin, file?.State, file?.IsPrivate));
    }

    [Fact]
    public async Task A_draft_holder_is_sent_as_cores_draft_owner()
    {
        var (store, handler) = Store(HttpStatusCode.Created, JsonSerializer.Serialize(new { id = Guid.NewGuid() }));
        var formId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var hold = await store.HoldAsync(Guid.NewGuid(), new FileHolder.Draft(formId, userId), userId);

        using var body = JsonDocument.Parse(handler.RequestBody!);
        var owner = body.RootElement.GetProperty("owner");
        Assert.Equal(FileStoreOutcome.Ok, hold.Outcome);
        Assert.Equal(("forms", "draft", $"{formId}:{userId}"), (owner.GetProperty("service").GetString(), owner.GetProperty("type").GetString(), owner.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task A_hold_core_refuses_is_reported_as_not_holdable() =>
        Assert.Equal(FileStoreOutcome.NotHoldable, (await Store(HttpStatusCode.UnprocessableEntity, "{}").Store.HoldAsync(Guid.NewGuid(), new FileHolder.Response(Guid.NewGuid()), null)).Outcome);
}
