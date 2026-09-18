using System.Net;
using System.Text;
using System.Text.Json;
using Skylab.Forms.Application;
using Skylab.Forms.Infrastructure.Auth;
using Xunit;

namespace Forms.Infrastructure.Tests;

public class CoreGuestApplyTests
{
    [Fact]
    public async Task Apply_posts_identity_to_core_guest_route_without_phone()
    {
        var eventId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"id":"t1","ticketType":"GUEST"}""");
        var apply = new CoreGuestApply(new HttpClient(handler) { BaseAddress = new Uri("http://core") });

        var ok = await apply.ApplyAsync(eventId, new EventGuestIdentity("Yusuf", "Acmaci", "yusuf@example.com"));

        Assert.True(ok);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal($"/v1/events/{eventId}/applications/guest", handler.Path);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("Yusuf", body.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("Acmaci", body.RootElement.GetProperty("lastName").GetString());
        Assert.Equal("yusuf@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.False(body.RootElement.TryGetProperty("phoneNumber", out _));
    }

    [Fact]
    public async Task Apply_treats_conflict_as_success_and_unauthorized_as_failure()
    {
        var eventId = Guid.NewGuid();
        var conflict = new CoreGuestApply(new HttpClient(new RecordingHandler(HttpStatusCode.Conflict, "{}"))
        {
            BaseAddress = new Uri("http://core")
        });
        Assert.True(await conflict.ApplyAsync(eventId, new EventGuestIdentity("Yusuf", "Acmaci", "yusuf@example.com")));

        var unauthorized = new CoreGuestApply(new HttpClient(new RecordingHandler(HttpStatusCode.Unauthorized, "{}"))
        {
            BaseAddress = new Uri("http://core")
        });
        Assert.False(await unauthorized.ApplyAsync(eventId, new EventGuestIdentity("Yusuf", "Acmaci", "yusuf@example.com")));
    }

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.PathAndQuery;
            if (request.Content is not null)
                Body = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
