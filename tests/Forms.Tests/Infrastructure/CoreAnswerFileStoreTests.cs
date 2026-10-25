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
    public async Task A_public_legacy_file_keeps_its_address()
    {
        var id = Guid.NewGuid();
        var (store, _) = Store(HttpStatusCode.OK, JsonSerializer.Serialize(new { id, name = "cv.pdf", size = 10, purpose = "legacy", status = "attached", visibility = "public", url = "https://cdn.example/cv.pdf" }));

        var file = (await store.FindAsync(id)).File;

        Assert.Equal((false, "https://cdn.example/cv.pdf"), (file?.IsPrivate, file?.PublicUrl));
    }

    [Fact]
    public async Task A_hold_core_refuses_is_reported_as_not_holdable() =>
        Assert.Equal(FileStoreOutcome.NotHoldable, (await Store(HttpStatusCode.UnprocessableEntity, "{}").Store.HoldAsync(Guid.NewGuid(), new FileHolder.Response(Guid.NewGuid()), null)).Outcome);

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, "{}", FileStoreOutcome.TooLarge)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, "{}", FileStoreOutcome.TypeNotAllowed)]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"media_name_invalid\"}", FileStoreOutcome.NameInvalid)]
    [InlineData(HttpStatusCode.BadRequest, "{\"code\":\"media_purpose_invalid\"}", FileStoreOutcome.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, "{\"retryAfterSeconds\":30}", FileStoreOutcome.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}", FileStoreOutcome.Unavailable)]
    public async Task A_refused_guest_upload_says_why(HttpStatusCode status, string problem, FileStoreOutcome expected)
    {
        var (store, _) = Store(status, problem);

        var upload = await store.UploadGuestFileAsync(new MemoryStream([1, 2, 3]), "cv.pdf", "application/pdf");

        Assert.Equal(expected, upload.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "{}", FileStoreOutcome.Scanning)]
    [InlineData(HttpStatusCode.Gone, "{\"scanResult\":\"infected\"}", FileStoreOutcome.Rejected)]
    [InlineData(HttpStatusCode.NotFound, "{}", FileStoreOutcome.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity, "{\"code\":\"media_link_subject_inactive\"}", FileStoreOutcome.SubjectInactive)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "{}", FileStoreOutcome.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", FileStoreOutcome.Failed)]
    public async Task A_refused_download_link_says_why(HttpStatusCode status, string problem, FileStoreOutcome expected)
    {
        var (store, _) = Store(status, problem);

        Assert.Equal(expected, (await store.CreateDownloadLinkAsync(Guid.NewGuid(), Guid.NewGuid())).Outcome);
    }
}
