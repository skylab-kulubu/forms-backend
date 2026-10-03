using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Abstractions;

namespace Skylab.Forms.Infrastructure.Mail;

public class MailWorker : BackgroundService
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ChannelMailDispatcher _dispatcher;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MailWorker> _logger;

    public MailWorker(ChannelMailDispatcher dispatcher, IServiceScopeFactory scopeFactory, ILogger<MailWorker> logger)
    {
        _dispatcher = dispatcher;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendWithRetryAsync(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mail worker beklenmeyen hata (template {TemplateId})", request.TemplateId);
            }
        }
    }

    private async Task SendWithRetryAsync(SingleMailRequest request, CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var scope = _scopeFactory.CreateScope();
            var mail = scope.ServiceProvider.GetRequiredService<ISkyMailService>();

            if (await mail.SendSingleAsync(request, ct))
                return;

            if (attempt < MaxAttempts)
                await Task.Delay(RetryDelay, ct);
        }

        _logger.LogError("Mail {MaxAttempts} denemede gönderilemedi (template {TemplateId})", MaxAttempts, request.TemplateId);
    }
}
