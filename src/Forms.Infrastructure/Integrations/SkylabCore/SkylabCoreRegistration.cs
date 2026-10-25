using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.ShortLinks;
using Skylab.Forms.Infrastructure.Integrations.Oidc;
using Skylab.Forms.Infrastructure.ResponseNotifications;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

internal sealed class SkylabCoreOptions
{
    public const string SectionName = "Integrations:SkylabCore";

    public string BaseUrl { get; set; } = "";
}

public static class SkylabCoreRegistration
{
    /// <summary>
    /// SKY LAB core'u Forms'a bağlar: kişiler, cevap dosyaları, kısa linkler, cevap bildirimleri ve hesap engeli.
    /// Adresi verilmezse bu yetenekler kapalıdır; hesap engelinin ayarı ayrıdır ve her durumda okunur.
    /// </summary>
    public static IServiceCollection AddSkylabCore(this IServiceCollection services, IConfiguration configuration)
    {
        AddAccountAccessGate(services, configuration);

        if (string.IsNullOrWhiteSpace(configuration[$"{SkylabCoreOptions.SectionName}:BaseUrl"]))
        {
            services.AddSingleton<IUserDirectory, UnconfiguredUserDirectory>();
            services.AddSingleton<IAnswerFileStore, UnconfiguredAnswerFileStore>();
            services.AddSingleton<IShortLinkProvider, UnconfiguredShortLinks>();
            services.AddSingleton(new IntegrationNotice(LogLevel.Warning,
                "Integrations:SkylabCore:BaseUrl boş: core kapalı; kişi bilgileri, cevap dosyaları ve kısa linkler kullanılamıyor"));

            return services;
        }

        services.AddOptions<SkylabCoreOptions>()
            .BindConfiguration(SkylabCoreOptions.SectionName)
            .Validate(core => HttpUrl.IsValid(core.BaseUrl), "Integrations:SkylabCore:BaseUrl must be the address of the core API, like http://core:8080.")
            .ValidateOnStart();
        services.AddOptions<ShortLinkOptions>()
            .Validate(links => HttpUrl.IsValid(links.FormsPublicUrl), "ShortLinks:FormsPublicUrl must be the public address of the forms site, since core's short links point to it.")
            .ValidateOnStart();
        services.RequireServiceAccount();

        services.AddHttpClient<IUserDirectory, CoreUserDirectory>(Core())
            .AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<IShortLinkProvider, CoreShortLinkProvider>(Core(TimeSpan.FromSeconds(10)))
            .AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient<IAnswerFileStore, CoreAnswerFileStore>(Core(TimeSpan.FromSeconds(60)))
            .AddHttpMessageHandler<ServiceTokenHandler>();

        services.AddHttpClient(ResponseNotificationWorker.HttpClientName, Core(TimeSpan.FromSeconds(10)))
            .AddHttpMessageHandler<ServiceTokenHandler>();
        services.AddHostedService<ResponseNotificationWorker>();

        return services;
    }

    private static Action<IServiceProvider, HttpClient> Core(TimeSpan? timeout = null) => (provider, client) =>
    {
        client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<SkylabCoreOptions>>().Value.BaseUrl);
        if (timeout is { } limit) client.Timeout = limit;
    };

    /// <summary>off modunda kapı hiç kaydedilmez; middleware ve hazırlık ucu bunu kapalı kapı sayar.</summary>
    private static void AddAccountAccessGate(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccountAccessGateOptions>().BindConfiguration(AccountAccessGateOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AccountAccessGateOptions>, AccountAccessGateOptionsValidator>();

        if (!string.Equals(configuration[$"{AccountAccessGateOptions.SectionName}:Mode"]?.Trim(), "enforce", StringComparison.OrdinalIgnoreCase)) return;

        services.AddSingleton<IAccountAccessPolicy>(provider =>
        {
            var gate = provider.GetRequiredService<IOptions<AccountAccessGateOptions>>().Value;
            return new AccountAccessGate(ConnectionMultiplexer.Connect(gate.ToRedisConfiguration()), gate);
        });
    }
}
