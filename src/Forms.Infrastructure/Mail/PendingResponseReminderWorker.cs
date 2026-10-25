using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Mail;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Infrastructure.Scheduling;

namespace Skylab.Forms.Infrastructure.Mail;

public class PendingResponseReminderWorker(
    IServiceScopeFactory scopeFactory,
    ICacheService cache,
    IMailDispatcher dispatcher,
    IOptions<FormMailOptions> options,
    ILogger<PendingResponseReminderWorker> logger) : ScheduledWorker(cache, logger)
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IMailDispatcher _dispatcher = dispatcher;
    private readonly FormMailOptions _options = options.Value;

    protected override string LockKey => "forms:pending-reminder:lock";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(_options.ReminderScanIntervalMinutes);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        string.IsNullOrEmpty(_options.PendingReminderTemplateKey) ? Task.CompletedTask : base.ExecuteAsync(stoppingToken);

    protected override async Task RunAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var responses = scope.ServiceProvider.GetRequiredService<IFormResponseRepository>();
        var userService = scope.ServiceProvider.GetRequiredService<IUserDirectory>();

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
            .Where(u => u.Email is not null)
            .ToDictionary(u => u.Id);

        foreach (var (reviewerId, forms) in byReviewer)
        {
            if (!reviewers.TryGetValue(reviewerId, out var reviewer)) continue;

            var variables = new Dictionary<string, object>(MailNames.Of(reviewer))
            {
                ["totalPending"] = forms.Sum(f => f.PendingCount),
                ["forms"] = forms.Select(f => (object)new Dictionary<string, object>
                {
                    ["formId"] = f.FormId.ToString(),
                    ["formTitle"] = f.FormTitle,
                    ["pendingCount"] = f.PendingCount
                }).ToList()
            };

            _dispatcher.Enqueue(new EmailMessage(_options.PendingReminderTemplateKey, reviewer.Email!, MailNames.Full(reviewer), variables));
        }

        await responses.MarkOverduePendingRemindedAsync(cutoff, DateTime.UtcNow, ct);
    }
}
