using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Skylab.Forms.Api.AccountAccess;
using Skylab.Forms.Api.Auth;
using Skylab.Forms.Infrastructure.AccountAccess;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Skylab.Forms.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountAccessGateIntegrationCollection :
    ICollectionFixture<AccountAccessRedisFixture>
{
    public const string Name = "account-access-gate-redis";
}

public sealed class AccountAccessRedisFixture : IAsyncLifetime
{
    public const string Password = "forms-access-gate-test-secret";
    public const int Database = 13;

    private readonly RedisContainer _container = new RedisBuilder("redis:7.4.2-alpine")
        .WithCommand("redis-server", "--requirepass", Password, "--appendonly", "no")
        .Build();

    public IConnectionMultiplexer Writer { get; private set; } = null!;
    public string Endpoint => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Writer = await ConnectionMultiplexer.ConnectAsync(
            $"{Endpoint},user=default,password={Password},defaultDatabase={Database},allowAdmin=true");
    }

    public async Task DisposeAsync()
    {
        await Writer.DisposeAsync();
        await _container.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await Writer.GetDatabase(Database).ExecuteAsync("FLUSHDB");
    }
}

[Collection(AccountAccessGateIntegrationCollection.Name)]
public sealed class AccountAccessGateIntegrationTests : IAsyncLifetime
{
    private static readonly Guid Subject =
        Guid.Parse(AccountAccessGateContract.GoldenSubject);
    private static readonly Guid FormId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly AccountAccessRedisFixture _redis;
    private readonly RSA _signingRsa = RSA.Create(2048);
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private SecurityKey _signingKey = null!;
    private int _sideEffects;

    public AccountAccessGateIntegrationTests(AccountAccessRedisFixture redis)
    {
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        await _redis.ResetAsync();
        (_app, _client, _signingKey) = await StartAppAsync(_redis.Endpoint);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
        _signingRsa.Dispose();
    }

    [Fact]
    public async Task Anonymous_public_reads_and_response_submission_skip_the_gate()
    {
        var readResponse = await _client.GetAsync($"/api/forms/{FormId}");
        var metaResponse = await _client.GetAsync($"/api/forms/{FormId}/meta");
        var sharedGroupResponse = await _client.GetAsync(
            $"/api/forms/component-groups/{FormId}/meta?token=shared");
        var sharedResponse = await _client.GetAsync(
            $"/api/forms/responses/{FormId}/meta?token=shared");
        var submitResponse = await _client.PostAsync(
            "/api/forms/responses",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, metaResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sharedGroupResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sharedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        Assert.Equal(5, _sideEffects);
    }

    [Fact]
    public async Task Allowed_authenticated_principal_reaches_the_optional_public_handler()
    {
        await WriteContractAsync();

        var response = await SendAuthenticatedAsync(HttpMethod.Get, $"/api/forms/{FormId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _sideEffects);
    }

    [Fact]
    public async Task A_preexisting_token_is_blocked_immediately_without_negative_caching()
    {
        await WriteContractAsync();
        var token = CreateToken();

        var beforeBlock = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/api/forms/{FormId}",
            token);
        await Database.StringSetAsync(
            AccountAccessGateContract.MarkerKey(Subject.ToString()),
            AccountAccessGateContract.MarkerValue);
        var afterBlock = await SendAuthenticatedAsync(
            HttpMethod.Get,
            $"/api/forms/{FormId}",
            token);

        Assert.Equal(HttpStatusCode.OK, beforeBlock.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterBlock.StatusCode);
        Assert.Equal(1, _sideEffects);
    }

    [Fact]
    public async Task Blocked_principal_gets_generic_401_before_any_handler_side_effect()
    {
        await WriteContractAsync();
        await Database.StringSetAsync(
            AccountAccessGateContract.MarkerKey(Subject.ToString()),
            AccountAccessGateContract.MarkerValue);

        var response = await SendAuthenticatedAsync(HttpMethod.Post, "/api/admin/forms/mutate");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, value => value.Scheme == "Bearer");
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal(0, _sideEffects);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Blocked_principal_cannot_turn_an_optional_public_route_into_an_authenticated_read()
    {
        await WriteContractAsync();
        await Database.StringSetAsync(
            AccountAccessGateContract.MarkerKey(Subject.ToString()),
            AccountAccessGateContract.MarkerValue);

        var response = await SendAuthenticatedAsync(HttpMethod.Get, $"/api/forms/{FormId}/meta");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _sideEffects);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-contract")]
    public async Task Missing_or_wrong_contract_fails_closed_without_side_effects(
        string? contractValue)
    {
        if (contractValue is not null)
        {
            await Database.StringSetAsync(
                AccountAccessGateContract.ContractKey,
                contractValue);
        }

        var response = await SendAuthenticatedAsync(HttpMethod.Post, "/api/admin/forms/mutate");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("1", response.Headers.RetryAfter?.ToString());
        Assert.Equal(0, _sideEffects);
    }

    [Fact]
    public async Task Malformed_marker_fails_closed_without_side_effects()
    {
        await WriteContractAsync();
        await Database.StringSetAsync(
            AccountAccessGateContract.MarkerKey(Subject.ToString()),
            "true");

        var response = await SendAuthenticatedAsync(HttpMethod.Post, "/api/admin/forms/mutate");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("1", response.Headers.RetryAfter?.ToString());
        Assert.Equal(0, _sideEffects);
    }

    [Fact]
    public async Task Invalid_credentials_are_rejected_before_the_gate_or_public_handler()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/forms/{FormId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "forged.token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _sideEffects);
    }

    [Fact]
    public async Task Redis_outage_returns_503_and_never_invokes_the_mutation()
    {
        var (outageApp, outageClient, _) = await StartAppAsync("127.0.0.1:1");
        try
        {
            using var request = AuthenticatedRequest(HttpMethod.Post, "/api/admin/forms/mutate");
            var response = await outageClient.SendAsync(request);
            var liveness = await outageClient.GetAsync("/health/live");
            var readiness = await outageClient.GetAsync("/health/ready");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.Equal("1", response.Headers.RetryAfter?.ToString());
            Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
            Assert.Equal(0, _sideEffects);
        }
        finally
        {
            outageClient.Dispose();
            await outageApp.DisposeAsync();
        }
    }

    [Fact]
    public async Task Liveness_is_process_only_and_readiness_checks_the_exact_contract()
    {
        var liveWithoutContract = await _client.GetAsync("/health/live");
        var authenticatedLiveWithoutContract = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/health/live");
        var notReady = await _client.GetAsync("/health/ready");
        await WriteContractAsync();
        await Database.StringSetAsync(
            AccountAccessGateContract.MarkerKey(Subject.ToString()),
            AccountAccessGateContract.MarkerValue);
        var ready = await _client.GetAsync("/health/ready");
        var authenticatedReadyForBlockedSubject = await SendAuthenticatedAsync(
            HttpMethod.Get,
            "/health/ready");

        Assert.Equal(HttpStatusCode.OK, liveWithoutContract.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticatedLiveWithoutContract.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, notReady.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticatedReadyForBlockedSubject.StatusCode);
        Assert.Equal(0, _sideEffects);
    }

    private IDatabase Database => _redis.Writer.GetDatabase(AccountAccessRedisFixture.Database);

    private Task WriteContractAsync() => Database.StringSetAsync(
        AccountAccessGateContract.ContractKey,
        AccountAccessGateContract.ContractValue);

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        HttpMethod method,
        string path,
        string? token = null)
    {
        using var request = AuthenticatedRequest(method, path, token);
        return await _client.SendAsync(request);
    }

    private HttpRequestMessage AuthenticatedRequest(
        HttpMethod method,
        string path,
        string? token = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? CreateToken());
        return request;
    }

    private async Task<(WebApplication App, HttpClient Client, SecurityKey SigningKey)> StartAppAsync(
        string endpoint)
    {
        var signingKey = new RsaSecurityKey(_signingRsa) { KeyId = "access-gate-test-key" };
        var validationKey = new RsaSecurityKey(_signingRsa.ExportParameters(false))
        {
            KeyId = signingKey.KeyId
        };

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(AccountAccessGateMiddleware).Assembly.GetName().Name,
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = FormsJwtAuthenticationExtensions.ExactIssuer,
            ["Authentication:Audience"] = FormsJwtAuthenticationExtensions.ExactAudience,
            ["AccountAccessGate:Mode"] = "enforce",
            ["AccountAccessGate:Endpoint"] = endpoint,
            ["AccountAccessGate:Username"] = "default",
            ["AccountAccessGate:Password"] = AccountAccessRedisFixture.Password,
            ["AccountAccessGate:Database"] = AccountAccessRedisFixture.Database.ToString(),
            ["AccountAccessGate:Tls"] = "false",
            ["AccountAccessGate:OperationTimeoutMilliseconds"] = "200",
            ["AccountAccessGate:RetryAfterSeconds"] = "1"
        });
        builder.Services.AddCors(options => options.AddPolicy("TestFrontend", policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
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
        builder.Services.AddAccountAccessGate(builder.Configuration);

        var app = builder.Build();
        app.UseFormsJwtAuthentication("TestFrontend");
        app.UseCors("TestFrontend");
        app.UseAccountAccessGate();
        app.MapAccountAccessHealthEndpoints();
        app.MapGet("/api/forms/{id:guid}", (Guid id) =>
        {
            Interlocked.Increment(ref _sideEffects);
            return Results.Ok();
        });
        app.MapGet("/api/forms/{id:guid}/meta", (Guid id) =>
        {
            Interlocked.Increment(ref _sideEffects);
            return Results.Ok();
        });
        app.MapGet("/api/forms/component-groups/{id:guid}/meta", (Guid id) =>
        {
            Interlocked.Increment(ref _sideEffects);
            return Results.Ok();
        });
        app.MapGet("/api/forms/responses/{id:guid}/meta", (Guid id) =>
        {
            Interlocked.Increment(ref _sideEffects);
            return Results.Ok();
        });
        app.MapPost("/api/forms/responses", () =>
        {
            Interlocked.Increment(ref _sideEffects);
            return Results.Created();
        });
        app.MapPost("/api/admin/forms/mutate", () =>
        {
            Interlocked.Increment(ref _sideEffects);
            return Results.NoContent();
        });

        await app.StartAsync();
        return (app, app.GetTestClient(), signingKey);
    }

    private string CreateToken()
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = FormsJwtAuthenticationExtensions.ExactIssuer,
            Audience = FormsJwtAuthenticationExtensions.ExactAudience,
            IssuedAt = now,
            NotBefore = now.AddSeconds(-5),
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Subject.ToString()
            }
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
