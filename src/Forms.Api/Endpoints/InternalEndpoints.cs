using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Infrastructure.AccountAccess;

namespace Skylab.Forms.Api.Endpoints;

/// <summary>
/// Diğer SKY LAB servislerinin iç Docker ağından çağırdığı uçlar. Her uç çağıran istemciyi
/// (azp) ve Forms istemcisindeki rolünü ister. Hatalar RFC 7807'dir ve istekten değer taşımaz.
/// </summary>
public static class InternalEndpoints
{
    private const int MaxErasureBodyBytes = 4096;
    private static readonly TimeSpan ErasureTimeout = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions StrictJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    private sealed record ErasureBody(
        [property: JsonPropertyName("request_id")] string RequestId,
        [property: JsonPropertyName("subject_id")] string SubjectId,
        [property: JsonPropertyName("emails")] string[] Emails);

    /// <summary>
    /// Traefik'ten gelen istek (X-Forwarded-*, Forwarded, X-Real-Ip) token'a bakılmadan 404 alır.
    /// Kimlik doğrulamadan önce çalışır.
    /// </summary>
    public static IApplicationBuilder UseInternalEndpointGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/internal") &&
                context.Request.Headers.Keys.Any(name =>
                    name.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("X-Real-Ip", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });

    public static void MapInternalEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("internal/v1").ExcludeFromDescription();

        // Core'un hesap silme komutu (core docs/account-erasure-command.md).
        group.MapPut("/account-erasures/{requestId}", EraseAccountAsync)
            .AddEndpointFilter(RequireCaller("core-erasure", "skyforms:account:erase", "erasure_forbidden"));
    }

    /// <summary>
    /// Token genel JwtBearer zincirinde doğrulandı (imza, issuer, süre, aud ∋ forms). Burada
    /// azp tam olarak çağıran istemci olmalı ve rol resource_access.forms.roles içinde olmalı.
    /// </summary>
    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireCaller(
        string clientId, string role, string forbiddenCode) =>
        async (invocation, next) =>
        {
            var user = invocation.HttpContext.User;
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            if (user.Identities.All(identity => !identity.IsAuthenticated))
            {
                invocation.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
            }

            return user.FindFirst("azp")?.Value == clientId && HasFormsRole(user.FindFirst("resource_access")?.Value, role)
                ? await next(invocation)
                : Problem(StatusCodes.Status403Forbidden, forbiddenCode);
        };

    private static bool HasFormsRole(string? resourceAccess, string role)
    {
        if (string.IsNullOrEmpty(resourceAccess)) return false;
        try
        {
            using var document = JsonDocument.Parse(resourceAccess);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("forms", out var client) &&
                   client.ValueKind == JsonValueKind.Object &&
                   client.TryGetProperty("roles", out var roles) &&
                   roles.ValueKind == JsonValueKind.Array &&
                   roles.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.String && r.GetString() == role);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<IResult> EraseAccountAsync(
        string requestId,
        HttpContext context,
        IAccountErasureService erasures,
        IAccountAccessGate gate,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Skylab.Forms.Api.AccountErasure");
        var ct = context.RequestAborted;

        var command = await ReadErasureCommandAsync(context.Request, requestId, ct);
        if (command is null)
            return NotDone(logger, "invalid", StatusCodes.Status400BadRequest, "invalid_erasure_command");

        try
        {
            var receipt = await erasures.FindReceiptAsync(command.RequestId, ct);
            if (receipt is null)
            {
                // Ele geçirilmiş bir çağıran, silme istememiş birinin verisini sildiremesin:
                // core kişiyi önce engeller, Forms engeli kendisi okur.
                if (gate.Mode == AccountAccessGateMode.Off)
                    return Unavailable(context, logger, command.RequestId, "subject_block_unverifiable", 300);

                switch (await gate.CheckSubjectAsync(command.SubjectId.ToString(), ct))
                {
                    case AccountAccessDecision.Allowed:
                        return NotDone(logger, command.RequestId.ToString(), StatusCodes.Status409Conflict, "subject_not_blocked");
                    case AccountAccessDecision.Unavailable:
                        return Unavailable(context, logger, command.RequestId, "subject_block_unverifiable", 30);
                }

                // İş isteğin iptaline bağlı değil: core 15 sn'de vazgeçse de iş commit edilir ve
                // core'un sonraki denemesi kayıtlı 200'ü alır.
                using var timeout = new CancellationTokenSource(ErasureTimeout);
                receipt = await erasures.EraseAsync(command, timeout.Token);
                logger.LogInformation("Account erasure {RequestId} completed: {Counts}", command.RequestId, receipt.Counts);
            }

            return Completed(receipt);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Hata mesajı bir değeri alıntılayabilir; yalnız türü ve SQLSTATE yazılır.
            logger.LogError("Account erasure {RequestId} failed: {ExceptionType} sqlstate {SqlState}",
                command.RequestId, ex.GetType().Name, (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState ?? "none");
            return Unavailable(context, logger, command.RequestId, "erasure_store_unavailable", 30);
        }
    }

    /// <summary>Gövde: tam olarak request_id (yoldakiyle aynı), subject_id ve 0–3 adres; en çok 4 KB.</summary>
    private static async Task<AccountErasureCommand?> ReadErasureCommandAsync(HttpRequest request, string requestId, CancellationToken ct)
    {
        if (!request.HasJsonContentType() || request.ContentLength is null or > MaxErasureBodyBytes ||
            !Guid.TryParseExact(requestId, "D", out var parsedRequestId))
            return null;

        ErasureBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<ErasureBody>(request.Body, StrictJson, ct);
        }
        catch (JsonException)
        {
            return null;
        }

        // Keycloak sub'ı küçük harfli tireli UUID'dir; başka yazılış hiçbir satırla eşleşmez.
        if (body is null || body.RequestId != requestId ||
            !Guid.TryParseExact(body.SubjectId, "D", out var subjectId) ||
            subjectId.ToString() != body.SubjectId || subjectId == DeletedUser.Id ||
            body.Emails.Length > 3 || !body.Emails.All(IsPlainAddress))
            return null;

        return new AccountErasureCommand(
            parsedRequestId, subjectId, body.Emails.Select(e => e.ToLowerInvariant()).Distinct().ToList());
    }

    /// <summary>Adresler metin içinde arandığı için açıkça adres olmayan değer reddedilir.</summary>
    private static bool IsPlainAddress(string? email) =>
        email is { Length: > 0 and <= 254 } &&
        !email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) &&
        email.LastIndexOf('@') is > 0 and var at && at < email.Length - 1;

    /// <summary>Makbuzdan yazılır; sayılar sıralı olduğu için tekrarı ilk cevapla aynı baytlardır.</summary>
    private static IResult Completed(AccountErasureReceipt receipt) => Results.Json(new
    {
        request_id = receipt.RequestId,
        status = "completed",
        completed_at = receipt.CompletedAt,
        counts = JsonSerializer.Deserialize<SortedDictionary<string, long>>(receipt.Counts)
    });

    private static IResult Unavailable(HttpContext context, ILogger logger, Guid requestId, string code, int retryAfterSeconds)
    {
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        return NotDone(logger, requestId.ToString(), StatusCodes.Status503ServiceUnavailable, code);
    }

    private static IResult NotDone(ILogger logger, string requestId, int status, string code)
    {
        logger.LogWarning("Account erasure {RequestId} not done: {Code}", requestId, code);
        return Problem(status, code);
    }

    private static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
