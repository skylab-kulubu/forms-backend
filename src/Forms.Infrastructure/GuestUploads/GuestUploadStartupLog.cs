using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Infrastructure.Integrations.Cloudflare;

namespace Skylab.Forms.Infrastructure.GuestUploads;

internal sealed class GuestUploadStartupLog : IHostedService
{
    private readonly GuestUploadSettings _settings;
    private readonly TurnstileOptions _turnstile;
    private readonly ILogger<GuestUploadStartupLog> _logger;

    public GuestUploadStartupLog(GuestUploadSettings settings, TurnstileOptions turnstile, ILogger<GuestUploadStartupLog> logger)
    {
        _settings = settings;
        _turnstile = turnstile;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_settings.Options.Enabled && !_turnstile.IsEnabled)
            _logger.LogWarning("FORMS_GUEST_UPLOADS=on ama TURNSTILE_SECRET_KEY boş; misafir dosya yükleme kapalı kalıyor");

        if (_turnstile.UsesTestSecret)
            _logger.LogWarning("TURNSTILE_SECRET_KEY bir Cloudflare test anahtarı; hostname ve action denetlenmiyor");

        _logger.LogInformation(
            "Turnstile doğrulaması {TurnstileState}, misafir dosya yükleme {GuestUploadState}",
            _turnstile.IsEnabled ? "on" : "off",
            _settings.Options.Enabled && _turnstile.IsEnabled ? "on" : "off");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
