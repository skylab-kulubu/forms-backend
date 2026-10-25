using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Infrastructure.Integrations.Oidc;

namespace Skylab.Forms.Infrastructure.Integrations.SkyMail;

internal sealed class SkyMailOptions
{
    public const string SectionName = "Integrations:SkyMail";

    public string BaseUrl { get; set; } = "";
}

public static class SkyMailRegistration
{
    /// <summary>Forms'un maillerini ve yanıtlayan listelerini SkyMail'e verir; adresi verilmezse mail kapalıdır.</summary>
    public static IServiceCollection AddSkyMail(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration[$"{SkyMailOptions.SectionName}:BaseUrl"]))
        {
            services.AddSingleton<UnconfiguredMail>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<UnconfiguredMail>());
            services.AddSingleton<IMailAudienceExport>(sp => sp.GetRequiredService<UnconfiguredMail>());
            services.AddOptions<FormMailOptions>().Validate(
                mail => !mail.SendsAny,
                "FormMail template keys need a mail provider: set Integrations:SkyMail:BaseUrl or leave the keys empty.");
            services.AddSingleton(new IntegrationNotice(LogLevel.Warning, "Integrations:SkyMail:BaseUrl boş: mail kapalı"));

            return services;
        }

        services.AddOptions<SkyMailOptions>()
            .BindConfiguration(SkyMailOptions.SectionName)
            .Validate(mail => HttpUrl.IsValid(mail.BaseUrl), "Integrations:SkyMail:BaseUrl must be the address of the SkyMail API, like http://skymail:3000/v1/.")
            .ValidateOnStart();
        services.RequireServiceAccount();

        services.AddHttpClient<SkyMailClient>((provider, client) =>
        {
            // Uçlar göreli yazılır; sonunda "/" olmayan bir adres son yol parçasını (v1) düşürürdü.
            var baseUrl = provider.GetRequiredService<IOptions<SkyMailOptions>>().Value.BaseUrl;
            client.BaseAddress = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<SkyMailClient>());
        services.AddTransient<IMailAudienceExport>(sp => sp.GetRequiredService<SkyMailClient>());

        return services;
    }
}
