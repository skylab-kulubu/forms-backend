using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Skylab.Forms.Api.Auth;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Auth;
using Xunit;

namespace Skylab.Forms.Api.Tests;

public sealed class JwtAuthenticationIntegrationTests : IAsyncLifetime
{
    private static readonly Guid Subject = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string AllowedOrigin = "https://forms.example.test";
    private readonly RSA _signingRsa = RSA.Create(2048);
    private readonly RSA _untrustedRsa = RSA.Create(2048);
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var signingKey = new RsaSecurityKey(_signingRsa) { KeyId = "trusted-test-key" };
        var validationKey = new RsaSecurityKey(_signingRsa.ExportParameters(false))
        {
            KeyId = signingKey.KeyId
        };

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FormsJwtAuthenticationExtensions).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = FormsJwtAuthenticationExtensions.ExactIssuer,
            ["Authentication:Audience"] = FormsJwtAuthenticationExtensions.ExactAudience,
            ["Authentication:ClockSkewSeconds"] = "30",
            ["Authentication:MetadataTimeoutSeconds"] = "5"
        });
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("TestFrontend", policy =>
            {
                policy.WithOrigins(AllowedOrigin).AllowAnyHeader().AllowAnyMethod();
            });
        });
        builder.Services.AddFormsJwtAuthentication(builder.Configuration);
        builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var openIdConfiguration = new OpenIdConnectConfiguration
            {
                Issuer = FormsJwtAuthenticationExtensions.ExactIssuer
            };
            openIdConfiguration.SigningKeys.Add(validationKey);
            options.ConfigurationManager =
                new StaticConfigurationManager<OpenIdConnectConfiguration>(openIdConfiguration);
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUserService, JwtCurrentUserService>();

        _app = builder.Build();
        _app.UseFormsJwtAuthentication("TestFrontend");
        _app.UseCors("TestFrontend");
        _app.MapGet("/swagger/index.html", () => Results.Text("Swagger UI"));
        _app.MapGet("/optional", async (ICurrentUserService currentUser, CancellationToken cancellationToken) =>
        {
            var userId = await currentUser.GetUserIdAsync(cancellationToken);
            var realmAdmin = await currentUser.HasRoleAsync("ADMIN", cancellationToken: cancellationToken);
            var formsAdmin = await currentUser.HasRoleAsync(
                "skyforms:*",
                "forms",
                cancellationToken);
            var legacyRole = await currentUser.HasRoleAsync(
                "legacy-role",
                "forms",
                cancellationToken);

            return Results.Json(new ProbeResponse(userId, realmAdmin, formsAdmin, legacyRole));
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();

        SigningKey = signingKey;
    }

    private SecurityKey SigningKey { get; set; } = null!;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _signingRsa.Dispose();
        _untrustedRsa.Dispose();
    }

    [Fact]
    public async Task Anonymous_endpoint_remains_available_without_credentials()
    {
        var response = await _client.GetAsync("/optional");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadProbeAsync(response);
        Assert.Null(body.UserId);
        Assert.False(body.RealmAdmin);
        Assert.False(body.FormsAdmin);
        Assert.False(body.LegacyRole);
    }

    [Fact]
    public async Task Validated_principal_supplies_subject_and_existing_role_mappings()
    {
        var response = await SendAsync(CreateToken());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadProbeAsync(response);
        Assert.Equal(Subject, body.UserId);
        Assert.True(body.RealmAdmin);
        Assert.True(body.FormsAdmin);
        Assert.True(body.LegacyRole);
    }

    [Fact]
    public async Task Invalid_bearer_credentials_never_downgrade_to_anonymous()
    {
        var now = DateTime.UtcNow;
        var invalidTokens = new[]
        {
            "not-a-jwt",
            CreateToken(sign: false),
            CreateToken(issuer: "https://attacker.invalid/realms/e-skylab"),
            CreateToken(audience: "core"),
            CreateToken(audience: "forms/"),
            CreateToken(
                issuedAt: now.AddHours(-2),
                notBefore: now.AddHours(-2),
                expires: now.AddMinutes(-5)),
            CreateToken(signingKey: new RsaSecurityKey(_untrustedRsa) { KeyId = "untrusted-test-key" })
        };

        foreach (var token in invalidTokens)
        {
            var response = await SendAsync(token);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Contains(
                "Bearer",
                response.Headers.WwwAuthenticate.Select(value => value.Scheme));
        }
    }

    [Fact]
    public async Task Empty_bearer_credential_is_rejected_on_anonymous_endpoint()
    {
        var response = await SendAuthorizationAsync("Bearer");


        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Basic Zm9vOmJhcg==")]
    [InlineData("Digest abc")]
    [InlineData(" Bearer token")]
    [InlineData("Bearer\ttoken")]
    [InlineData("Bearer  token")]
    [InlineData("Bearer token ")]
    public async Task Any_noncanonical_authorization_header_is_rejected(string authorization)
    {
        var response = await SendAuthorizationAsync(authorization);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Blank_authorization_header_is_invalid_not_anonymous()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = string.Empty;

        var state = FormsJwtAuthenticationExtensions.GetCredentialState(context.Request);

        Assert.Equal(FormsJwtAuthenticationExtensions.CredentialState.Invalid, state);
    }

    [Fact]
    public void Duplicate_authorization_values_are_invalid_before_authentication()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = new StringValues(
            ["Bearer first", "Bearer second"]);

        var state = FormsJwtAuthenticationExtensions.GetCredentialState(context.Request);

        Assert.Equal(FormsJwtAuthenticationExtensions.CredentialState.Invalid, state);
    }

    [Fact]
    public async Task Combined_basic_and_bearer_header_is_rejected()
    {
        var response = await SendAuthorizationAsync($"Basic Zm9vOmJhcg==, Bearer {CreateToken()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Coalesced_bearer_credentials_are_rejected()
    {
        var token = CreateToken();

        var response = await SendAuthorizationAsync($"Bearer {token}, Bearer {token}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_authorization_header_values_are_rejected()
    {
        var token = CreateToken();

        var response = await SendAuthorizationValuesAsync(
            [$"Bearer {token}", $"Bearer {token}"]);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_is_public_without_credentials_but_rejects_invalid_authorization()
    {
        var anonymousResponse = await _client.GetAsync("/swagger/index.html");
        var invalidResponse = await SendAuthorizationAsync(
            "Basic Zm9vOmJhcg==",
            path: "/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task Authentication_challenges_keep_cors_headers_for_the_allowed_frontend()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/optional");
        request.Headers.TryAddWithoutValidation("Authorization", "Basic Zm9vOmJhcg==");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Authorization_header_on_preflight_cannot_bypass_credential_validation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/optional");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
        request.Headers.TryAddWithoutValidation("Authorization", "Basic Zm9vOmJhcg==");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Credential_free_preflight_remains_available()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/optional");
        request.Headers.TryAddWithoutValidation("Origin", AllowedOrigin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AllowedOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData("Authentication:Issuer", "https://e.yildizskylab.com/realms/another")]
    [InlineData("Authentication:Audience", "core")]
    public void Startup_rejects_noncanonical_identity_boundary(string key, string value)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = FormsJwtAuthenticationExtensions.ExactIssuer,
            ["Authentication:Audience"] = FormsJwtAuthenticationExtensions.ExactAudience,
            [key] = value
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddFormsJwtAuthentication(configuration));

        Assert.Contains(key, exception.Message);
    }

    private async Task<HttpResponseMessage> SendAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/optional");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendAuthorizationAsync(
        string authorization,
        string path = "/optional")
    {
        return await SendAuthorizationValuesAsync([authorization], path);
    }

    private async Task<HttpResponseMessage> SendAuthorizationValuesAsync(
        IReadOnlyCollection<string> authorizations,
        string path = "/optional")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", authorizations);
        return await _client.SendAsync(request);
    }

    private string CreateToken(
        string issuer = FormsJwtAuthenticationExtensions.ExactIssuer,
        string audience = FormsJwtAuthenticationExtensions.ExactAudience,
        DateTime? issuedAt = null,
        DateTime? notBefore = null,
        DateTime? expires = null,
        bool sign = true,
        SecurityKey? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var credentials = sign
            ? new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.RsaSha256)
            : null;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt ?? now,
            NotBefore = notBefore ?? now.AddSeconds(-5),
            Expires = expires ?? now.AddMinutes(5),
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Subject.ToString(),
                ["realm_access"] = new Dictionary<string, object>
                {
                    ["roles"] = new[] { "ADMIN" }
                },
                ["resource_access"] = new Dictionary<string, object>
                {
                    ["forms"] = new Dictionary<string, object>
                    {
                        ["roles"] = new[] { "skyforms:*" }
                    },
                    ["dotnet"] = new Dictionary<string, object>
                    {
                        ["roles"] = new[] { "legacy-role" }
                    }
                }
            }
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private static async Task<ProbeResponse> ReadProbeAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return (await JsonSerializer.DeserializeAsync<ProbeResponse>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    private sealed record ProbeResponse(
        Guid? UserId,
        bool RealmAdmin,
        bool FormsAdmin,
        bool LegacyRole);
}
