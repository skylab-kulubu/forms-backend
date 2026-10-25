using System.Net;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Infrastructure.Integrations.SkylabCore;

namespace Skylab.Forms.Tests.Infrastructure;

public class CoreUserDirectoryTests
{
    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class CountingLogger<T> : ILogger<T>
    {
        public int Warnings { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Warnings++;
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, 0)]
    [InlineData(HttpStatusCode.InternalServerError, 1)]
    public async Task Only_a_core_failure_is_logged_as_one(HttpStatusCode status, int warnings)
    {
        var logger = new CountingLogger<CoreUserDirectory>();
        var directory = new CoreUserDirectory(new HttpClient(new StubHandler(status)) { BaseAddress = new Uri("http://core.test") }, logger);

        var user = await directory.GetUserAsync(Guid.NewGuid());

        Assert.Equal((null, warnings), (user, logger.Warnings));
    }
}
