using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;

namespace Skylab.Forms.Infrastructure.Mail;

public class PendingResponseReminderWorker : BackgroundService
{
    private const string LockKey = "forms:pending-reminder:lock";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICacheService _cache;
    private readonly IMailDispatcher _dispatcher;
    private readonly FormMailOptions _options;
    private readonly ILogger<PendingResponseReminderWorker> _logger;

    public PendingResponseReminderWorker(IServiceScopeFactory scopeFactory, ICacheService cache, IMailDispatcher dispatcher, IOptions<FormMailOptions> options, ILogger<PendingResponseReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _dispatcher = dispatcher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_options.PendingReminderTemplateId)) return;

        var interval = TimeSpan.FromMinutes(Math.Max(_options.ReminderScanIntervalMinutes, 1));
        using var timer = new PeriodicTimer(interval);

        do
        {
            try
            {
                if (await _cache.AcquireLockAsync(LockKey, interval, stoppingToken))
                    await ScanAndNotifyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bekleyen cevap hatırlatma taraması başarısız");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ScanAndNotifyAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var responses = scope.ServiceProvider.GetRequiredService<IFormResponseRepository>();
        var userService = scope.ServiceProvider.GetRequiredService<IExternalUserService>();

        var cutoff = DateTime.UtcNow.AddHours(-_options.ReminderThresholdHours);
        var overdueForms = await responses.GetOverduePendingByFormAsync(cutoff, ct);
        if (overdueForms.Count == 0) return;

        var byReviewer = new Dictionary<Guid, List<OverduePendingFormProjection>>();
        foreach (var form in overdueForms)
        {
            foreach (var reviewerId in form.ReviewerIds)
            {
                if (!byReviewer.TryGetValue(reviewerId, out var forms))
                    byReviewer[reviewerId] = forms = [];
                forms.Add(form);
            }
        }

        var reviewers = (await userService.GetUsersAsync(byReviewer.Keys, ct))
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .ToDictionary(u => u.Id);

        foreach (var (reviewerId, forms) in byReviewer)
        {
            if (!reviewers.TryGetValue(reviewerId, out var reviewer)) continue;

            var variables = new Dictionary<string, object>
            {
                ["recipientName"] = reviewer.FullName ?? string.Empty,
                ["totalPending"] = forms.Sum(f => f.PendingCount),
                ["forms"] = forms.Select(f => (object)new Dictionary<string, object>
                {
                    ["formId"] = f.FormId.ToString(),
                    ["formTitle"] = f.FormTitle,
                    ["pendingCount"] = f.PendingCount
                }).ToList()
            };

            _dispatcher.Enqueue(new SingleMailRequest(_options.PendingReminderTemplateId, reviewer.Email!, reviewer.FullName ?? string.Empty, variables));
        }

        await responses.MarkOverduePendingRemindedAsync(cutoff, DateTime.UtcNow, ct);
    }
}
