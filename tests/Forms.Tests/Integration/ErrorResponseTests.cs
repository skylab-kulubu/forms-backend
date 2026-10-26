using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    private sealed class FailingTail(Func<Exception> failure) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Run(_ => throw failure());
        };
    }

    private HttpClient FailingClient(Func<Exception>? failure = null) =>
        api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddTransient<IStartupFilter>(_ => new FailingTail(failure ?? (() => new InvalidOperationException("beklenmeyen"))))))
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

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest, "İstek okunamadı.")]
    [InlineData(StatusCodes.Status413PayloadTooLarge, "İstek gövdesi izin verilen boyutu aşıyor.")]
    public async Task A_request_the_server_cannot_read_keeps_its_own_status_instead_of_an_unexpected_500(int status, string message)
    {
        var response = await FailingClient(() => new BadHttpRequestException("Kestrel isteği okuyamadı.", status)).GetAsync("/api/forms/yarim-govde");

        var body = await response.Content.ReadFromJsonAsync<ServiceResult<object>>();
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal((ServiceStatus.NotAcceptable, message), (body!.Status, body.Message));
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
