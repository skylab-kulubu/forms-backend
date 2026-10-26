using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StackExchange.Redis;

namespace Skylab.Forms.Tests.Integration;

/// <summary>Core'un engel listesi sözleşmesi (core: docs/account-access-gate.md), Forms'un okuduğu hâliyle.</summary>
[Collection(ApiCollection.Name)]
public class AccountAccessGateTests(FormsApiFactory api)
{
    private const int GateDatabase = 5;

    [Fact]
    public async Task A_person_core_blocked_under_the_tokens_issuer_is_refused()
    {
        var gated = WithEnforcedGate();
        var blocked = Guid.NewGuid();
        await BlockAsync(blocked);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(gated, TestTokens.For(blocked)).GetAsync("/api/admin/forms/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(gated, TestTokens.For(Guid.NewGuid())).GetAsync("/api/admin/forms/")).StatusCode);
    }

    [Fact]
    public async Task Core_erases_a_person_only_after_blocking_them()
    {
        var gated = WithEnforcedGate();
        var core = ClientFor(gated, TestTokens.Service("core-erasure", "skyforms:account:erase"));
        var person = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await EraseAsync(core, person)).StatusCode);

        await BlockAsync(person);

        Assert.Equal(HttpStatusCode.OK, (await EraseAsync(core, person)).StatusCode);
    }

    private WebApplicationFactory<Program> WithEnforcedGate() =>
        api.WithWebHostBuilder(builder => builder
            .UseSetting("AccountAccessGate:Mode", "enforce")
            .UseSetting("AccountAccessGate:Endpoint", api.RedisEndpoint)
            .UseSetting("AccountAccessGate:Username", "default")
            .UseSetting("AccountAccessGate:Password", "unused")
            .UseSetting("AccountAccessGate:Database", GateDatabase.ToString())
            .UseSetting("AccountAccessGate:Tls", "false")
            // Paylaşılan Redis bütün paket koşarken yavaşlayabilir; varsayılan 200 ms kapıyı "okunamadı"ya düşürürdü.
            .UseSetting("AccountAccessGate:OperationTimeoutMilliseconds", "2000"));

    private async Task BlockAsync(Guid subject)
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(api.RedisEndpoint);
        var database = redis.GetDatabase(GateDatabase);
        var marker = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{TestTokens.Issuer}\0{subject}")));

        await database.StringSetAsync("skylab:account-access:v1:contract", "sha256(iss\\0sub);marker=1;ttl=none");
        await database.StringSetAsync("skylab:account-access:v1:blocked:" + marker, "1");
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        return client;
    }

    // İç uç gövdenin uzunluğunu ister; JsonContent uzunluk bildirmediği için gövde metin olarak gider.
    private static Task<HttpResponseMessage> EraseAsync(HttpClient core, Guid person)
    {
        var requestId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new { request_id = requestId.ToString(), subject_id = person.ToString(), emails = Array.Empty<string>() });

        return core.PutAsync($"/internal/v1/account-erasures/{requestId}", new StringContent(body, Encoding.UTF8, "application/json"));
    }
}
