using System.Text.Json;

namespace Skylab.Forms.Infrastructure.Outbox;

public interface IOutboxHandler
{
    string Type { get; }

    TimeSpan KeepTrying { get; }

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
