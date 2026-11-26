using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.AccountErasure;
using Skylab.Forms.Application.Contracts.ResponseReads;
using Skylab.Forms.Application.Services.AccountErasure;
using Skylab.Forms.Application.Services.ResponseReads;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Infrastructure.Auth;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Api.Endpoints;

/// <summary>
/// Diğer SKY LAB servislerinin iç Docker ağından çağırdığı uçlar. Hatalar RFC 7807'dir ve istekten değer
/// taşımaz.
/// </summary>
public static class InternalEndpoints
{
    private const int MaxErasureBodyBytes = 4096;
    private const int DefaultReadLimit = 100;
    private const int MaxReadLimit = 200;
    private const int MaxReadFields = 20;
    private const int MaxFieldIdLength = 100;
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
        group.MapPut("/account-erasures/{requestId}", async (string requestId, HttpContext context, IAccountErasureService service, TimeProvider time, ILoggerFactory loggerFactory, CancellationToken ct) =>
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
                !FormsPermissions.Has(context.User, FormsPermissions.AccountErasure))
                return Problem(StatusCodes.Status403Forbidden, "erasure_forbidden");

            var command = await ReadErasureCommandAsync(context.Request, requestId, ct);
            if (command is null)
                return Problem(StatusCodes.Status400BadRequest, "invalid_erasure_command");

            // Ele geçirilmiş bir çağıran, silme istememiş birinin verisini sildiremesin: core kişiyi önce engeller,
            // Forms engeli kendisi okur. Kapı yalnız enforce modunda kayıtlıdır; engel okunamıyorsa iş yapılmaz.
            var gate = context.RequestServices.GetService<IAccountAccessPolicy>();
            if (gate is null)
            {
                logger.LogWarning("Hesap silme {RequestId} yapılmadı: engel kapısı kapalı", command.RequestId);
                context.Response.Headers.RetryAfter = "300";
                return Problem(StatusCodes.Status503ServiceUnavailable, "subject_block_unverifiable");
            }

            var decision = context.User.FindFirst("iss")?.Value is { Length: > 0 } issuer
                ? await gate.CheckAsync(issuer, command.SubjectId.ToString(), ct)
                : AccountAccessDecision.Unavailable;
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
                    completed_at = time.GetUtcNow().UtcDateTime,
                    counts
                });
            }
            catch (Exception ex)
            {
                // Hata mesajı istekteki bir değeri (adres) alıntılayabilir; yalnız tür ve SQLSTATE yazılır.
                var sqlState = StorageErrors.CodeOf(ex) ?? "yok";
                logger.LogError("Hesap silme {RequestId} başarısız: {ExceptionType}, SQLSTATE {SqlState}", command.RequestId, ex.GetType().Name, sqlState);

                context.Response.Headers.RetryAfter = "30";
                return Problem(StatusCodes.Status503ServiceUnavailable, "erasure_store_unavailable");
            }
        });

        // Core'un form okuması: etkinliğe bağlanan formun soruları, cevap eşlemesi için.
        group.MapGet("/forms/{formId:guid}", async (Guid formId, HttpContext context, IResponseReadService service, CancellationToken ct) =>
        {
            if (RefuseRead(context) is { } refused) return refused;

            var form = await service.GetFormAsync(formId, ct);
            return form is null ? Problem(StatusCodes.Status404NotFound, "form_not_found") : Results.Json(form);
        });

        // Core'un cevap listesi: geriye dönük bilet ve günlük yakalama. Sıra (değişim anı, Id) artan, imleç opak.
        group.MapGet("/forms/{formId:guid}/responses", async (Guid formId, HttpContext context, IResponseReadService service, CancellationToken ct) =>
        {
            if (RefuseRead(context) is { } refused) return refused;

            var query = ReadListQuery(context.Request);
            if (query is null)
                return Problem(StatusCodes.Status400BadRequest, "invalid_query");

            var page = await service.ListAsync(formId, query, ct);
            return page is null ? Problem(StatusCodes.Status404NotFound, "form_not_found") : Results.Json(page);
        });

        // Core'un tek cevap okuması: misafirin adı ve adresi ile eşlenen soruların cevapları olayda değil burada gelir.
        group.MapGet("/forms/{formId:guid}/responses/{responseId:guid}", async (Guid formId, Guid responseId, HttpContext context, IResponseReadService service, CancellationToken ct) =>
        {
            if (RefuseRead(context) is { } refused) return refused;

            var fields = ReadFields(context.Request);
            if (fields is null)
                return Problem(StatusCodes.Status400BadRequest, "invalid_query");

            var response = await service.GetAsync(formId, responseId, fields, ct);
            return response is null ? Problem(StatusCodes.Status404NotFound, "response_not_found") : Results.Json(response);
        });
    }

    /// <summary>Token JwtBearer zincirinde doğrulandı; okuma yalnız core'un servis hesabına (azp core, skyforms:responses:read-service).</summary>
    private static IResult? RefuseRead(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return Problem(StatusCodes.Status401Unauthorized, "unauthorized");
        }

        if (context.User.FindFirst("azp")?.Value != "core" ||
            !FormsPermissions.Has(context.User, FormsPermissions.ResponsesRead))
            return Problem(StatusCodes.Status403Forbidden, "read_forbidden");

        return null;
    }

    /// <summary>status yalnız accepted olabilir; changedSince RFC 3339; limit 1–200; fields virgülle en çok 20 soru Id'si.</summary>
    private static ResponseReadQuery? ReadListQuery(HttpRequest request)
    {
        var status = request.Query["status"].ToString();
        if (status is not ("" or "accepted")) return null;

        DateTime? changedSince = null;
        var since = request.Query["changedSince"].ToString();
        if (since.Length > 0)
        {
            if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) return null;
            changedSince = parsed.UtcDateTime;
        }

        ResponseReadCursor? after = null;
        var cursor = request.Query["after"].ToString();
        if (cursor.Length > 0 && (after = ResponseReadCursor.Parse(cursor)) is null) return null;

        var limit = DefaultReadLimit;
        var limitText = request.Query["limit"].ToString();
        if (limitText.Length > 0 && (!int.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxReadLimit))
            return null;

        var fields = ReadFields(request);
        return fields is null ? null : new ResponseReadQuery(status == "accepted", changedSince, after, limit, fields);
    }

    private static IReadOnlyList<string>? ReadFields(HttpRequest request)
    {
        var fields = request.Query["fields"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .ToList();

        return fields.Count > MaxReadFields || fields.Any(field => field.Length > MaxFieldIdLength) ? null : fields;
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
