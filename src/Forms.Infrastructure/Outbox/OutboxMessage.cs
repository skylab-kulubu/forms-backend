using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skylab.Forms.Infrastructure.Outbox;

public class OutboxMessage
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }

    public static OutboxMessage Of<T>(string type, T payload, DateTime now) => new()
    {
        Type = type,
        Payload = JsonSerializer.Serialize(payload, Json),
        CreatedAt = now,
        NextAttemptAt = now
    };
}

public static class OutboxTypes
{
    public const string ResponseStatusChanged = "response.status-changed";
}
