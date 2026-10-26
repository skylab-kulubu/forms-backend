using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.GuestUploads;
using Skylab.Forms.Application.GuestUploads;

namespace Skylab.Forms.Api.Extensions;

/// <summary>
/// Misafir dosya yüklemesinin ilk savunma hattı: dosya gövdesi (52 MiB'a kadar) bağlanmadan önce istemci adresine göre
/// sayar. Redis'teki sayaçlar asıl sınırdır; bu sınır örnek başınadır ve yalnız gövdeyi hiç okumadan reddetmek içindir.
/// </summary>
public static class GuestUploadRateLimit
{
    public const string Policy = "guest-upload";

    public static IServiceCollection AddGuestUploadRateLimit(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                GuestUploadKeys.ClientSubject(context.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = GuestRateLimits.IpFilesPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

            options.OnRejected = (rejection, ct) =>
            {
                var retryAfter = rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? (int)Math.Ceiling(wait.TotalSeconds) : 60;
                rejection.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

                var refusal = new ServiceResult<GuestUploadContract>(
                    ServiceStatus.TooManyRequests,
                    new GuestUploadContract(Reason: GuestUploadReason.TooManyUploads, RetryAfterSeconds: retryAfter),
                    "Kısa sürede çok fazla dosya yüklendi. Biraz sonra tekrar deneyin.");

                return new ValueTask(refusal.ToApiResult().ExecuteAsync(rejection.HttpContext));
            };
        });
}
