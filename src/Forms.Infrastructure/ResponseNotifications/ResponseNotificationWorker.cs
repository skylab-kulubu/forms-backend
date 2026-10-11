using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Infrastructure.Storage;

namespace Skylab.Forms.Infrastructure.ResponseNotifications;

/// <summary>
/// Bekleyen cevap bildirimlerini core'a gönderir; core etkinlik biletini bunlardan yazar. Core aynı
/// bildirimi iki kez almaktan etkilenmez, bu yüzden ulaşılamazsa bildirim yedi gün boyunca artan
/// aralıklarla yeniden denenir. Core'daki uç ya da rol henüz yokken gelen bildirimler de böylece kaybolmaz.
/// </summary>
public class ResponseNotificationWorker : BackgroundService
{
    public const string HttpClientName = "core-responses";

    private const string LockKey = "forms:response-notifications:lock";
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    // Core yanıt vermezse bir tur 50 x 10 sn sürer; kilit ondan uzun tutulur ki iki örnek aynı satırları işlemesin.
    private static readonly TimeSpan LockTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(7);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ResponseNotificationWorker> _logger;

    public ResponseNotificationWorker(IServiceScopeFactory scopeFactory, ICacheService cache, IHttpClientFactory httpClientFactory, ILogger<ResponseNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                if (await _cache.AcquireLockAsync(LockKey, LockTtl, stoppingToken))
                {
                    try
                    {
                        await SendDueAsync(stoppingToken);
                    }
                    finally
                    {
                        await _cache.ReleaseLockAsync(LockKey, CancellationToken.None);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cevap bildirimlerinin taraması başarısız");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FormsDbContext>();
        var now = DateTime.UtcNow;

        var dropped = await db.ResponseNotifications.Where(n => n.CreatedAt < now - GiveUpAfter).ExecuteDeleteAsync(ct);
        if (dropped > 0)
            _logger.LogWarning("{Count} cevap bildirimi yedi günde core'a ulaşamadığı için bırakıldı", dropped);

        var due = await db.ResponseNotifications
            .Where(n => n.NextAttemptAt <= now)
            .OrderBy(n => n.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        var client = _httpClientFactory.CreateClient(HttpClientName);

        foreach (var notification in due)
        {
            if (await TrySendAsync(client, notification, ct))
            {
                db.ResponseNotifications.Remove(notification);
            }
            else
            {
                notification.Attempts++;
                notification.NextAttemptAt = now + Backoff(notification.Attempts);
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> TrySendAsync(HttpClient client, ResponseNotification notification, CancellationToken ct)
    {
        try
        {
            using var content = new StringContent(notification.Payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync($"/v1/forms/{notification.FormId}/responses", content, ct);

            if (response.IsSuccessStatusCode) return true;

            _logger.LogWarning("Core cevap bildirimi {NotificationId} için {Status} döndü; yeniden denenecek", notification.Id, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Bildirim misafirin adını ve e-postasını taşır; log'a yalnız kimliği yazılır.
            _logger.LogWarning("Cevap bildirimi {NotificationId} core'a gönderilemedi ({Error}); yeniden denenecek", notification.Id, ex.Message);
            return false;
        }
    }

    private static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempts - 1), MaxBackoff.TotalSeconds));
}
