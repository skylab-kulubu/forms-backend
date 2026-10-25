using Microsoft.AspNetCore.Hosting;

namespace Skylab.Forms.Tests.Integration;

[Collection(ApiCollection.Name)]
public class StartupSettingsTests(FormsApiFactory api)
{
    [Theory]
    [InlineData("Authentication:Issuer", "", "Authentication:Issuer")]
    [InlineData("AccountAccessGate:Mode", "", "AccountAccessGate:Mode")]
    [InlineData("Cors:AllowedOrigin", "https://forms.example.org/", "Cors:AllowedOrigin")]
    [InlineData("GuestUploads:Mode", "maybe", "GuestUploads:Mode")]
    [InlineData("Integrations:SkyMail:BaseUrl", "", "FormMail template keys need a mail provider")]
    [InlineData("Integrations:SkylabCore:BaseUrl", "http://core.test", "ShortLinks:FormsPublicUrl")]
    public void A_missing_or_wrong_setting_stops_startup_naming_the_setting(string key, string value, string named)
    {
        using var broken = api.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        var failure = Assert.ThrowsAny<Exception>(() => broken.CreateClient());

        Assert.Contains(named, failure.ToString());
    }
}
