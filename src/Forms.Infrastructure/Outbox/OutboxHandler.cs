using System.Text.Json;

namespace Skylab.Forms.Infrastructure.Outbox;

/// <summary>Bir türdeki outbox mesajlarını teslim eder; sağlayıcısı kapalı türün işleyicisi kayıtlı değildir.</summary>
public interface IOutboxHandler
{
    string Type { get; }

    /// <summary>Bu süreden eski mesajdan vazgeçilir.</summary>
    TimeSpan KeepTrying { get; }

    /// <returns>true: mesaj teslim edildi ya da kalıcı olarak geri çevrildi, silinir; false: sonra yeniden denenir.</returns>
    Task<bool> HandleAsync(string payload, CancellationToken ct);
}

public abstract class OutboxHandler<T> : IOutboxHandler
{
    public abstract string Type { get; }

    public abstract TimeSpan KeepTrying { get; }

    public Task<bool> HandleAsync(string payload, CancellationToken ct) =>
        HandleAsync(JsonSerializer.Deserialize<T>(payload, OutboxMessage.Json) ?? throw new JsonException("Boş outbox mesajı"), ct);

    protected abstract Task<bool> HandleAsync(T message, CancellationToken ct);
}
