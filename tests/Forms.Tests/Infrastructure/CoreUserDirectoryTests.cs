using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Integrations.SkylabCore;

namespace Skylab.Forms.Tests.Infrastructure;

public class CoreUserDirectoryTests
{
    private sealed class StubHandler(HttpStatusCode status, string? json = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json") });
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

    [Theory]
    [InlineData(HttpStatusCode.NotFound, null, UserLookupOutcome.NotFound, 0)]
    [InlineData(HttpStatusCode.InternalServerError, null, UserLookupOutcome.Unavailable, 1)]
    [InlineData(HttpStatusCode.OK, """{"user_id":"5d1c7a52-8f0e-4b8e-9c55-0f6d2a3b9e11"}""", UserLookupOutcome.Unavailable, 1)]
    [InlineData(HttpStatusCode.OK, """{"id":"5d1c7a52-8f0e-4b8e-9c55-0f6d2a3b9e11","email":"ayse@ornek.com","status":"active"}""", UserLookupOutcome.Found, 0)]
    public async Task A_lookup_tells_a_missing_person_from_an_unreadable_core(HttpStatusCode status, string? json, UserLookupOutcome outcome, int warnings)
    {
        var logger = new CountingLogger<CoreUserDirectory>();
        var directory = new CoreUserDirectory(new HttpClient(new StubHandler(status, json)) { BaseAddress = new Uri("http://core.test") }, logger);

        var lookup = await directory.FindUserAsync(Guid.NewGuid());

        Assert.Equal((outcome, warnings), (lookup.Outcome, logger.Warnings));
    }
}
