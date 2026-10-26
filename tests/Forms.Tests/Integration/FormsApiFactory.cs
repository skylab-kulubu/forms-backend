using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.AnswerFiles;
using Skylab.Forms.Infrastructure.Attempts;
using Skylab.Forms.Infrastructure.Mail;
using Skylab.Forms.Infrastructure.Outbox;
using Skylab.Forms.Tests.Integration.Fakes;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Skylab.Forms.Tests.Integration;

/// <summary>
/// API'yi gerçek Postgres ve Redis ile açar. Core ayarlanmaz, yani dosyalar ve kısa linkler Forms'un sağlayıcısız
/// hâliyle çalışır; kişiler ve mailler bellekteki sahtelerde durur. Testler aynı veritabanını paylaşır, bu yüzden
/// her test kendi kullanıcılarını ve formlarını yaratır.
/// </summary>
public sealed class FormsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Arka plan işleri core'a ve SkyMail'e gider; bir test bir işe ihtiyaç duyarsa onu kendisi çağırır.
    internal static readonly Type[] Workers =
    [
        typeof(OutboxDispatcher),
        typeof(AnswerFileLinkWorker),
        typeof(PendingResponseReminderWorker),
        typeof(FormAttemptExpiryWorker)
    ];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public FakeUserDirectory Users { get; } = new();
    public RecordingMailDispatcher Mail { get; } = new();

    public string RedisEndpoint => _redis.GetConnectionString();

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public HttpClient ClientFor(Guid userId, params string[] formsRoles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, formsRoles));

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // UseSetting geliştiricinin kabuğundaki ortam değişkenlerinin de önüne geçer; davranışı değiştiren her ayar burada sabit.
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ConnectionStrings:Forms"] = _postgres.GetConnectionString(),
            ["Redis:ConnectionString"] = _redis.GetConnectionString(),
            ["Authentication:Issuer"] = TestTokens.Issuer,
            ["Authentication:Roles:Clients:0"] = "forms",
            ["Authentication:Roles:Clients:1"] = "dotnet",
            ["Authentication:Roles:PlatformAdmin"] = "skyforms:*",
            ["Authentication:Roles:AccountErasure"] = "skyforms:account:erase",
            ["AccountAccessGate:Mode"] = "off",
            ["Integrations:SkylabCore:BaseUrl"] = "",
            ["Integrations:Turnstile:SecretKey"] = "",
            ["GuestUploads:Mode"] = "off",

            // Mail açık: gönderilecek mailler RecordingMailDispatcher'da görünür, SkyMail'e hiçbir şey gitmez.
            ["Integrations:SkyMail:BaseUrl"] = "http://skymail.test/v1/",
            ["Authentication:ServiceAccount:TokenUrl"] = "http://auth.test/token",
            ["Authentication:ServiceAccount:ClientId"] = "forms",
            ["Authentication:ServiceAccount:ClientSecret"] = "unused",
            ["FormMail:FormCopyTemplateKey"] = "forms.form-copy",
            ["FormMail:StatusChangedTemplateKey"] = "forms.status-changed",
            ["FormMail:PendingReminderTemplateKey"] = "forms.pending-reminder",
            ["FormMail:AttemptUpdateTemplateKey"] = "forms.attempt-update"
        })
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            TrustTestTokens(services);
            RemoveWorkers(services);

            services.RemoveAll<IUserDirectory>();
            services.AddSingleton<IUserDirectory>(Users);
            services.RemoveAll<IMailDispatcher>();
            services.AddSingleton<IMailDispatcher>(Mail);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, UnreachableMail>();
            services.RemoveAll<IMailAudienceExport>();
            services.AddSingleton<IMailAudienceExport, UnreachableMail>();
        });
    }

    /// <summary>Authority'siz statik yapılandırma: token metadata indirilmeden test anahtarıyla doğrulanır.</summary>
    internal static void TrustTestTokens(IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.Configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
            options.Configuration.SigningKeys.Add(TestTokens.SigningKey);
            options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
            options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
        });

    internal static void RemoveWorkers(IServiceCollection services)
    {
        foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) && Workers.Contains(service.ImplementationType)).ToList())
            services.Remove(worker);
    }

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<FormsApiFactory>
{
    public const string Name = "api";
}
