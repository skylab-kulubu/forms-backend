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
using Skylab.Forms.Infrastructure.ResponseNotifications;
using Skylab.Forms.Tests.Integration.Fakes;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Skylab.Forms.Tests.Integration;

/// <summary>
/// API'yi gerçek Postgres ve Redis ile açar; core, SkyMail ve Keycloak'ın yerinde bellekteki sahteler durur.
/// Testler aynı veritabanını paylaşır, bu yüzden her test kendi kullanıcılarını ve formlarını yaratır.
/// </summary>
public sealed class FormsApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Arka plan işleri core'a ve SkyMail'e gider; bir test bir işe ihtiyaç duyarsa onu kendisi çağırır.
    private static readonly Type[] Workers =
    [
        typeof(ResponseNotificationWorker),
        typeof(AnswerFileLinkWorker),
        typeof(MailWorker),
        typeof(PendingResponseReminderWorker),
        typeof(FormAttemptExpiryWorker)
    ];

    // Uygulama bu ayarları ortam değişkeninden doğrudan okuyor; geliştiricinin kabuğundaki bir değer test
    // veritabanının ya da Redis'in yerine geçmesin.
    private static readonly string[] AmbientNames = ["CONNECTION_STRING", "Redis__ConnectionString", "ALLOWED_ORIGIN", "TRUSTED_PROXY_RANGES"];
    private static readonly string[] AmbientPrefixes = ["ACCOUNT_ACCESS_", "TURNSTILE_", "FORMS_GUEST_"];

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    public FormsApiFactory()
    {
        foreach (var name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (AmbientNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || AmbientPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                Environment.SetEnvironmentVariable(name, null);
        }
    }

    public FakeUserDirectory Users { get; } = new();
    public RecordingMailDispatcher Mail { get; } = new();

    public string RedisEndpoint => _redis.GetConnectionString();

    public HttpClient ClientFor(Guid userId, params string[] formsRoles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.For(userId, formsRoles));

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Forms", _postgres.GetConnectionString());
        builder.UseSetting("Redis:ConnectionString", _redis.GetConnectionString());

        builder.ConfigureTestServices(services =>
        {
            // Authority'siz statik yapılandırma: token metadata indirilmeden test anahtarıyla doğrulanır.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.Configuration = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                options.Configuration.SigningKeys.Add(TestTokens.SigningKey);
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
            });

            services.RemoveAll<IUserDirectory>();
            services.AddSingleton<IUserDirectory>(Users);
            services.RemoveAll<IMailDispatcher>();
            services.AddSingleton<IMailDispatcher>(Mail);
            services.RemoveAll<ICoreMedia>();
            services.AddSingleton<ICoreMedia, UnreachableCoreMedia>();
            services.RemoveAll<ICoreShortLinks>();
            services.AddSingleton<ICoreShortLinks, UnreachableCoreShortLinks>();
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender, UnreachableMail>();
            services.RemoveAll<IMailAudienceExport>();
            services.AddSingleton<IMailAudienceExport, UnreachableMail>();

            foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) && Workers.Contains(service.ImplementationType)).ToList())
                services.Remove(worker);
        });
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
