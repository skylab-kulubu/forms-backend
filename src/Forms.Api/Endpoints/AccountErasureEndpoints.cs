using System.Security.Claims;
using System.Text.Json;
using Npgsql;
using Skylab.Forms.Api.AccountErasure;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Infrastructure.AccountAccess;

namespace Skylab.Forms.Api.Endpoints;

/// <summary>
/// Core'un hesap silme komutu (ADR-0051; core docs/account-erasure-command.md).
/// Yanıt biçimi sözleşmeden gelir, Forms'un ServiceResult zarfını kullanmaz. Loglar
/// yalnız komut kimliğini ve sabit bir kodu taşır: gövde, kişinin kimliği, adresi ya da
/// adı hiçbir yola yazılmaz.
/// </summary>
public static class AccountErasureEndpoints
{
    public const string CallerClientId = "core-erasure";
    public const string ResourceClient = "forms";
    public const string EraseRole = "skyforms:account:erase";

    // Core Retry-After'ı 30 sn ile 15 dk arasına sıkıştırır.
    private const int RetryAfterSeconds = 30;
    private const int GateOffRetryAfterSeconds = 300;
    private static readonly TimeSpan ErasureWorkTimeout = TimeSpan.FromSeconds(90);

    public static IEndpointRouteBuilder MapAccountErasureEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPut("/internal/v1/account-erasures/{requestId}", EraseAsync)
            .ExcludeFromDescription();

        return routes;
    }

    private static async Task EraseAsync(
        HttpContext context,
        string requestId,
        IAccountErasureService erasures,
        IAccountAccessGate gate,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("Skylab.Forms.Api.AccountErasure");
        var ct = context.RequestAborted;
        context.Response.Headers.CacheControl = "no-store";

        var validPath = AccountErasureCommandReader.TryParseRequestId(requestId, out var pathRequestId);
        var logId = validPath ? pathRequestId.ToString() : "invalid";

        // İmza, issuer, süre ve aud ∋ forms genel JwtBearer zincirinde doğrulandı;
        // geçersiz token buraya ulaşmadan 401 aldı. Çağıranın kendi engel kontrolünü de
        // hesap erişim kapısı yaptı.
        if (context.User.Identities.All(identity => !identity.IsAuthenticated))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await ProblemAsync(context, logger, logId, StatusCodes.Status401Unauthorized, "erasure_unauthorized", "Erasure token rejected");
            return;
        }

        if (!IsCoreErasure(context.User))
        {
            await ProblemAsync(context, logger, logId, StatusCodes.Status403Forbidden, "erasure_forbidden", "Erasure forbidden");
            return;
        }

        var command = await AccountErasureCommandReader.ReadAsync(context.Request, ct);
        if (!validPath || command is null || command.RequestId != pathRequestId)
        {
            await ProblemAsync(context, logger, logId, StatusCodes.Status400BadRequest, "invalid_erasure_command", "Invalid erasure command");
            return;
        }

        try
        {
            var receipt = await erasures.FindCompletedAsync(command.RequestId, ct);
            if (receipt is not null)
            {
                await CompletedAsync(context, receipt);
                return;
            }

            // Ele geçirilmiş bir çağıran, silme istememiş birinin verisini sildiremesin:
            // core kişiyi komuttan önce engeller, Forms bu engeli kendisi okur. Kapı
            // kapalıyken (sandbox) okuyamaz.
            if (gate.Mode == AccountAccessGateMode.Off)
            {
                await UnavailableAsync(context, logger, logId, "subject_block_unverifiable", GateOffRetryAfterSeconds);
                return;
            }

            switch (await gate.CheckSubjectAsync(command.SubjectId.ToString(), ct))
            {
                case AccountAccessDecision.Blocked:
                    break;
                case AccountAccessDecision.Allowed:
                    await ProblemAsync(context, logger, logId, StatusCodes.Status409Conflict, "subject_not_blocked", "Subject is not blocked");
                    return;
                default:
                    await UnavailableAsync(context, logger, logId, "subject_block_unverifiable", RetryAfterSeconds);
                    return;
            }

            // İş, isteğin iptaline bağlanmaz: core 15 sn'de vazgeçse de yapılan iş commit
            // edilir ve core'un sonraki denemesi kayıtlı 200'ü alır. Üst sınır sunucunun.
            using (var work = new CancellationTokenSource(ErasureWorkTimeout))
            {
                receipt = await erasures.EraseAsync(command, work.Token);
            }

            logger.LogInformation(
                "Account erasure {RequestId} completed: {Counts}",
                logId,
                string.Join(", ", receipt.Counts.Select(count => $"{count.Key}={count.Value}")));
            await CompletedAsync(context, receipt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Hata mesajı bir değeri alıntılayabilir; yalnız türü ve SQLSTATE yazılır.
            var sqlState = exception is PostgresException postgres ? postgres.SqlState : "none";
            logger.LogError(
                "Account erasure {RequestId} store failed: {ExceptionType} sqlstate {SqlState}",
                logId,
                exception.GetType().Name,
                sqlState);
            await UnavailableAsync(context, logger, logId, "erasure_store_unavailable", RetryAfterSeconds);
        }
    }

    /// <summary>
    /// azp tam olarak core-erasure, aud forms'u içerir ve Forms istemcisinin rolleri
    /// silme rolünü taşır. Başka bir istemcideki aynı adlı rol sayılmaz.
    /// </summary>
    private static bool IsCoreErasure(ClaimsPrincipal user)
    {
        if (!string.Equals(user.FindFirst("azp")?.Value, CallerClientId, StringComparison.Ordinal))
            return false;

        if (!user.FindAll("aud").Any(claim => string.Equals(claim.Value, ResourceClient, StringComparison.Ordinal)))
            return false;

        var resourceAccess = user.FindFirst("resource_access")?.Value;
        if (string.IsNullOrEmpty(resourceAccess))
            return false;

        try
        {
            using var document = JsonDocument.Parse(resourceAccess);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty(ResourceClient, out var client) &&
                   client.ValueKind == JsonValueKind.Object &&
                   client.TryGetProperty("roles", out var roles) &&
                   roles.ValueKind == JsonValueKind.Array &&
                   roles.EnumerateArray().Any(role =>
                       role.ValueKind == JsonValueKind.String &&
                       string.Equals(role.GetString(), EraseRole, StringComparison.Ordinal));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makbuzdan yazılır; anahtarlar sıralı olduğu için ilk cevap ve her tekrarı aynı
    /// baytlardır.
    /// </summary>
    private static async Task CompletedAsync(HttpContext context, AccountErasureReceiptContract receipt)
    {
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";

        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("request_id", receipt.RequestId.ToString("D"));
        writer.WriteString("status", "completed");
        writer.WriteString("completed_at", receipt.CompletedAt.ToUniversalTime());
        writer.WriteStartObject("counts");
        foreach (var (key, value) in receipt.Counts.OrderBy(count => count.Key, StringComparer.Ordinal))
            writer.WriteNumber(key, value);
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync();
    }

    private static Task UnavailableAsync(HttpContext context, ILogger logger, string logId, string code, int retryAfterSeconds)
    {
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        return ProblemAsync(context, logger, logId, StatusCodes.Status503ServiceUnavailable, code, "Erasure temporarily unavailable");
    }

    /// <summary>RFC 7807; sabit bir code taşır, istekten hiçbir değeri geri yazmaz.</summary>
    private static async Task ProblemAsync(HttpContext context, ILogger logger, string logId, int status, string code, string title)
    {
        logger.LogWarning("Account erasure {RequestId} not done: {Code}", logId, code);

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("type", "about:blank");
        writer.WriteString("title", title);
        writer.WriteNumber("status", status);
        writer.WriteString("code", code);
        writer.WriteEndObject();
        await writer.FlushAsync();
    }
}
