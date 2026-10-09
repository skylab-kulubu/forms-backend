using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Infrastructure.AccountAccess;

namespace Skylab.Forms.Api.Endpoints;

/// <summary>
/// Diğer SKY LAB servislerinin iç Docker ağından çağırdığı uçlar. Hatalar RFC 7807'dir ve istekten değer
/// taşımaz.
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
    /// Traefik'ten gelen istek (X-Forwarded-*, Forwarded, X-Real-Ip) token'a bakılmadan 404 alır. Forwarded
    /// headers middleware'i X-Forwarded-For'u işleyip kaldırdığı için ondan önce çalışmalı.
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
        group.MapPut("/account-erasures/{requestId}", async (string requestId, HttpContext context, IAccountErasureService service, ICurrentUserService userService, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("Skylab.Forms.Api.AccountErasure");
            context.Response.Headers.CacheControl = "no-store";

            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.Headers.WWWAuthenticate = "Bearer";
                return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
            }

            // Token JwtBearer zincirinde doğrulandı (imza, issuer, süre, aud ∋ forms).
            if (context.User.FindFirst("azp")?.Value != "core-erasure" ||
                !await userService.HasRoleAsync("skyforms:account:erase", "forms", ct))
                return Problem(StatusCodes.Status403Forbidden, "erasure_forbidden");

            var command = await ReadErasureCommandAsync(context.Request, requestId, ct);
            if (command is null)
                return Problem(StatusCodes.Status400BadRequest, "invalid_erasure_command");

            // Ele geçirilmiş bir çağıran, silme istememiş birinin verisini sildiremesin: core kişiyi önce engeller,
            // Forms engeli kendisi okur. Kapı yalnız enforce modunda kayıtlıdır; engel okunamıyorsa iş yapılmaz.
            var gate = context.RequestServices.GetService<AccountAccessGate>();
            if (gate is null)
            {
                logger.LogWarning("Hesap silme {RequestId} yapılmadı: engel kapısı kapalı", command.RequestId);
                context.Response.Headers.RetryAfter = "300";
                return Problem(StatusCodes.Status503ServiceUnavailable, "subject_block_unverifiable");
            }

            var decision = await gate.CheckSubjectAsync(command.SubjectId.ToString(), ct);
            if (decision == AccountAccessDecision.Unavailable)
            {
                logger.LogWarning("Hesap silme {RequestId} yapılmadı: engel okunamadı", command.RequestId);
                context.Response.Headers.RetryAfter = "30";
                return Problem(StatusCodes.Status503ServiceUnavailable, "subject_block_unverifiable");
            }

            if (decision != AccountAccessDecision.Blocked)
            {
                logger.LogWarning("Hesap silme {RequestId} yapılmadı: kişi engelli değil", command.RequestId);
                return Problem(StatusCodes.Status409Conflict, "subject_not_blocked");
            }

            try
            {
                // İş isteğin iptaline (ct) değil 90 sn'lik bütçeye bağlı: core 15 sn'de vazgeçse de iş commit edilir;
                // core'un sonraki denemesi 200 ve 0 sayılar alır (sözleşme §10).
                using var timeout = new CancellationTokenSource(ErasureTimeout);
                var counts = await service.EraseAsync(command, timeout.Token);
                logger.LogInformation("Hesap silme {RequestId} tamamlandı: {Counts}", command.RequestId, counts);

                return Results.Json(new
                {
                    request_id = command.RequestId,
                    status = "completed",
                    completed_at = DateTime.UtcNow,
                    counts
                });
            }
            catch (Exception ex)
            {
                // Hata mesajı istekteki bir değeri (adres) alıntılayabilir; yalnız tür ve SQLSTATE yazılır.
                var sqlState = (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState ?? "yok";
                logger.LogError("Hesap silme {RequestId} başarısız: {ExceptionType}, SQLSTATE {SqlState}", command.RequestId, ex.GetType().Name, sqlState);

                context.Response.Headers.RetryAfter = "30";
                return Problem(StatusCodes.Status503ServiceUnavailable, "erasure_store_unavailable");
            }
        });
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

        if (body is null || body.RequestId != requestId || body.Emails.Length > 3 || !body.Emails.All(IsPlainAddress))
            return null;

        // Keycloak sub'ı küçük harfli tireli UUID'dir; başka yazılış hiçbir satırla eşleşmez.
        if (!Guid.TryParseExact(body.SubjectId, "D", out var subjectId) || subjectId.ToString() != body.SubjectId || subjectId == DeletedUser.Id)
            return null;

        var emails = body.Emails.Select(email => email.ToLowerInvariant()).Distinct().ToList();
        return new AccountErasureCommand(parsedRequestId, subjectId, emails);
    }

    /// <summary>Adresler metin içinde arandığı için açıkça adres olmayan değer reddedilir.</summary>
    private static bool IsPlainAddress(string? email)
    {
        if (string.IsNullOrEmpty(email) || email.Length > 254) return false;
        if (email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return false;

        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1;
    }

    private static IResult Problem(int status, string code) =>
        Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
