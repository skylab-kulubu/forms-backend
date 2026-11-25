using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Skylab.Forms.Infrastructure.Outbox;

namespace Skylab.Forms.Infrastructure.Integrations.RabbitMq;

/// <summary>
/// Giden kutusundaki olayları forms.events'e, routing key olay türü olarak yayımlar. Kanal yayıncı onayıyla açılır:
/// satır yalnız broker mesajı kalıcı olarak aldığında (basic.ack) silinir; ret, kopma ya da zaman aşımı satırı bırakır
/// ve dispatcher yeniden dener. Exchange'i ve kuyrukları ops kurar; Forms hiçbir şey tanımlamaz.
/// </summary>
internal sealed class RabbitMqEventPublisher(IOptions<RabbitMqOptions> options, TimeProvider time, ILogger<RabbitMqEventPublisher> logger)
    : OutboxHandler<IntegrationEvent>, IAsyncDisposable
{
    private const string Exchange = "forms.events";
    private const int MaxBodyBytes = 64 * 1024;

    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectPause = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private DateTime _connectAfter;

    public override string Type => OutboxTypes.Event;

    public override TimeSpan KeepTrying => TimeSpan.FromDays(7);

    protected override async Task<bool> HandleAsync(IntegrationEvent message, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, OutboxMessage.Json);
        if (body.Length > MaxBodyBytes)
        {
            logger.LogError("Olay {EventId} ({Type}) {Size} bayt, 64 KiB sınırını aşıyor; yayımlanmadan bırakılıyor", message.Id, message.Type, body.Length);
            return true;
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = IntegrationEvent.ContentType,
            MessageId = message.Id.ToString(),
            Type = message.Type,
            AppId = "forms"
        };

        await _gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PublishTimeout);

            if (await OpenAsync(timeout.Token) is not { } channel) return false;

            try
            {
                await channel.BasicPublishAsync(Exchange, message.Type, mandatory: false, properties, body, timeout.Token);
                return true;
            }
            catch
            {
                // Onayı gelmeyen kanal bir sonraki denemede yeniden açılır.
                await CloseAsync();
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel?> OpenAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        // Broker'a ulaşılamadığında partideki her olay bağlantı zaman aşımını yeniden beklemesin.
        var now = time.GetUtcNow().UtcDateTime;
        if (now < _connectAfter) return null;

        await CloseAsync();
        _connectAfter = now + ReconnectPause;

        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.Host,
            Port = settings.Port,
            VirtualHost = settings.VirtualHost,
            UserName = settings.Username,
            Password = settings.Password,
            ClientProvidedName = "forms",
            AutomaticRecoveryEnabled = false,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        _connection = await factory.CreateConnectionAsync(ct);
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        _connectAfter = default;

        return _channel;
    }

    private async Task CloseAsync()
    {
        var channel = _channel;
        var connection = _connection;
        _channel = null;
        _connection = null;

        try
        {
            if (channel is not null) await channel.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Kopmuş bağlantı kapanırken de hata atabilir; zaten bırakılıyor.
            logger.LogDebug("RabbitMQ bağlantısı kapatılırken {Error}", ex.GetType().Name);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _gate.Dispose();
    }
}
