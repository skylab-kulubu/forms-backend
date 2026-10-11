using System.Text.Json;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Infrastructure.ResponseNotifications;

/// <summary>
/// Core'a gönderilmeyi bekleyen bir cevap bildirimi; core 2xx dönünce silinir.
/// </summary>
public class ResponseNotification
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }

    /// <summary>Henüz kesinleşmemiş (geçici) cevap için null; o, teslim edildiğinde bildirilir.</summary>
    public static ResponseNotification? For(FormResponse response, DateTime now)
    {
        // Silinmiş kullanıcıya geçmiş yanıtın durumu sonradan değişse de core onun adına bilet yazmamalı.
        if (response.UserId == DeletedUser.Id) return null;

        var status = response.Status switch
        {
            FormResponseStatus.NonRestrict or FormResponseStatus.Approved => "accepted",
            FormResponseStatus.Pending or FormResponseStatus.Flagged => "pending",
            FormResponseStatus.Declined => "declined",
            _ => null
        };

        if (status is null) return null;

        var payload = new
        {
            responseId = response.Id,
            status,
            userId = response.UserId,
            guest = response.Guest is { } guest ? new { guest.FirstName, guest.LastName, guest.Email } : null
        };

        return new ResponseNotification
        {
            FormId = response.FormId,
            Payload = JsonSerializer.Serialize(payload, Json),
            CreatedAt = now,
            NextAttemptAt = now
        };
    }
}
