using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skylab.Forms.Infrastructure.Outbox;

/// <summary>
/// forms.events'e giden olay, CloudEvents 1.0 yapılı JSON olarak. Giden kutusuna bu haliyle yazılır ve aynen yayımlanır;
/// Id giden kutusu satırının kimliğidir, yeniden denemede değişmez, dinleyen tekrarı buna göre eler.
/// </summary>
public sealed record IntegrationEvent(
    [property: JsonPropertyName("specversion")] string SpecVersion,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("time")] DateTime Time,
    [property: JsonPropertyName("datacontenttype")] string DataContentType,
    [property: JsonPropertyName("dataschema")] string DataSchema,
    [property: JsonPropertyName("data")] JsonElement Data,
    [property: JsonPropertyName("traceparent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TraceParent)
{
    public const string ContentType = "application/cloudevents+json";

    internal static OutboxMessage Outbox<T>(string type, T data, DateTime now, string source)
    {
        var id = Guid.NewGuid();

        // Olaya yol açan isteğin W3C izi; arka plan işlerinde yok.
        var trace = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

        var message = OutboxMessage.Of(
            OutboxTypes.Event,
            new IntegrationEvent("1.0", id, "/forms", type, now, "application/json", $"urn:skylab:schema:{type}:1",
                JsonSerializer.SerializeToElement(data, OutboxMessage.Json), trace),
            now,
            source);

        message.Id = id;
        return message;
    }
}
