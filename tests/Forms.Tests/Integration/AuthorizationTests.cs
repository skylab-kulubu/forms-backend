using System.Net;

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
}
