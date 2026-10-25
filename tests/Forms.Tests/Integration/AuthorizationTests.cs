using System.Net;
using System.Net.Http.Json;
using Skylab.Forms.Application.Common;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthorizationTests(FormsApiFactory api)
{
    [Fact]
    public async Task Only_a_platform_admin_lists_every_form()
    {
        Assert.Equal(HttpStatusCode.OK, (await api.ClientFor(Guid.NewGuid(), "skyforms:*").GetAsync("/api/admin/forms/all")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(Guid.NewGuid(), "skyforms:account:erase").GetAsync("/api/admin/forms/all")).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/forms/")]
    [InlineData("/api/admin/workflows/")]
    [InlineData("/api/forms/responses/draft/3f2b9c56-8f52-4d6e-9a43-1f4c55f0f1a2")]
    public async Task A_request_without_a_token_gets_401_in_the_service_envelope(string url)
    {
        var response = await api.CreateClient().GetAsync(url);
        var body = await response.Content.ReadFromJsonAsync<ServiceResult<object>>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(ServiceStatus.Unauthorized, body?.Status);
    }

    [Fact]
    public async Task A_signed_in_member_gets_403_in_the_service_envelope_from_an_admin_list()
    {
        var response = await api.ClientFor(Guid.NewGuid()).GetAsync("/api/admin/workflows/orphaned");
        var body = await response.Content.ReadFromJsonAsync<ServiceResult<object>>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ServiceStatus.NotAuthorized, body?.Status);
    }
}
