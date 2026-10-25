using System.Net;
using System.Text;
using Skylab.Forms.Infrastructure.Auth;

namespace Skylab.Forms.Tests.Infrastructure;

public class CoreShortLinkProviderTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    [Theory]
    [InlineData("{\"since\":\"2026-10-01T00:00:00Z\",\"total\":0}")]
    [InlineData("{\"since\":\"2026-10-01T00:00:00Z\",\"total\":0,\"sources\":null}")]
    public async Task Stats_without_sources_count_no_sources(string body)
    {
        var provider = new CoreShortLinkProvider(new HttpClient(new StubHandler(body)) { BaseAddress = new Uri("http://core.test") });

        var stats = await provider.GetStatsAsync(Guid.NewGuid());

        Assert.Equal((0, 0), (stats?.Total, stats?.Sources.Count));
    }
}
