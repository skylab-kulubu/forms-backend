using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ErrorResponseTests(FormsApiFactory api)
{
    /// <summary>Hiçbir uçla eşleşmeyen istek, uygulamanın hattından geçtikten sonra burada patlar.</summary>
    private sealed class FailingTail : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Run(_ => throw new InvalidOperationException("beklenmeyen"));
        };
    }

    private HttpClient FailingClient() =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddTransient<IStartupFilter, FailingTail>()))
            .CreateClient();

    [Fact]
    public async Task An_unexpected_error_on_the_public_api_answers_500_with_the_envelope()
    {
        var response = await FailingClient().GetAsync("/api/forms/patlayan");

        var body = await response.Content.ReadFromJsonAsync<ServiceResult<object>>();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(ServiceStatus.UnexpectedError, body!.Status);
        Assert.False(string.IsNullOrEmpty(body.Message));
    }

    [Fact]
    public async Task An_unexpected_error_on_an_internal_route_answers_500_with_a_problem()
    {
        var response = await FailingClient().GetAsync("/internal/v1/patlayan");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("unexpected_error", problem!.Extensions["code"]?.ToString());
    }
}
