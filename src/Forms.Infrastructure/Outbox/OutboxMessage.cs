using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skylab.Forms.Infrastructure.Outbox;

/// <summary>
/// Bir işle aynı transaction'da yazılan, sonra teslim edilen mesaj. Teslim edilince silinir; işleyicisi vazgeçene kadar
/// artan aralıklarla yeniden denenir.
/// </summary>
public class OutboxMessage
{
    // Tür adları ve enum'lar metin olarak yazılır: bekleyen mesaj bir sonraki sürümde de okunabilsin.
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }

    /// <summary>Mesajı üreten kayıt; tabloya yazılmaz, aynı değişikliğin olayının iki kez eklenmesini önler.</summary>
    internal string? Source { get; init; }

    public static OutboxMessage Of<T>(string type, T payload, DateTime now) => Of(type, payload, now, null);

    internal static OutboxMessage Of<T>(string type, T payload, DateTime now, string? source) => new()
    {
        Type = type,
        Payload = JsonSerializer.Serialize(payload, Json),
        CreatedAt = now,
        NextAttemptAt = now,
        Source = source
    };
}

public static class OutboxTypes
{
    public const string ResponseStatusChanged = "response.status-changed";
    public const string Mail = "mail.send";
}
