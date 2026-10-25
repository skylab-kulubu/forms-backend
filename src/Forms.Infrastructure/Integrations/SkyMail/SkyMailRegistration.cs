using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Integrations.Oidc;

namespace Skylab.Forms.Infrastructure.Integrations.SkyMail;

public static class SkyMailRegistration
{
    public static IServiceCollection AddSkyMail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<SkyMailClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:SkyMail:BaseUrl"] ?? "http://skymail:3000/v1/");
        }).AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddTransient<IEmailSender>(sp => sp.GetRequiredService<SkyMailClient>());
        services.AddTransient<IMailAudienceExport>(sp => sp.GetRequiredService<SkyMailClient>());

        return services;
    }
}
