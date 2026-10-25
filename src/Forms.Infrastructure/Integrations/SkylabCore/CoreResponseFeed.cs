using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Events;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Infrastructure.Outbox;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

/// <summary>
/// Forms'un cevap olaylarını core'a bildirir; core etkinlik biletini bunlardan yazar. Core aynı bildirimi iki kez almaktan
/// etkilenmez, bu yüzden ulaşılamazsa bildirim yedi gün boyunca yeniden denenir; core'daki uç ya da rol henüz yokken
/// gelen bildirimler de böylece kaybolmaz.
/// </summary>
internal sealed class CoreResponseFeed(HttpClient httpClient, ILogger<CoreResponseFeed> logger) : OutboxHandler<ResponseStatusChanged>
{
    public override string Type => OutboxTypes.ResponseStatusChanged;

    public override TimeSpan KeepTrying => TimeSpan.FromDays(7);

    protected override async Task<bool> HandleAsync(ResponseStatusChanged change, CancellationToken ct)
    {
        // Silinmiş kullanıcıya geçmiş yanıtın durumu sonradan değişse de core onun adına bilet yazmamalı.
        if (change.UserId == DeletedUser.Id) return true;

        var status = change.Status switch
        {
            FormResponseStatus.NonRestrict or FormResponseStatus.Approved => "accepted",
            FormResponseStatus.Pending or FormResponseStatus.Flagged => "pending",
            // Düzeltme istenen cevap kabul edilmeyecek; yerini alan yeni gönderim ayrıca bildirilir.
            FormResponseStatus.Declined or FormResponseStatus.Returned => "declined",
            _ => null
        };

        if (status is null) return true;

        var body = new
        {
            responseId = change.ResponseId,
            status,
            userId = change.UserId,
            // Adsız misafir gitmez: core misafir biletini ad ve soyadsız yazmaz, 400 döner.
            guest = change.Guest is { HasName: true } guest ? new { guest.FirstName, guest.LastName, guest.Email } : null
        };

        using var response = await httpClient.PostAsJsonAsync($"/v1/forms/{change.FormId}/responses", body, ct);
        if (response.IsSuccessStatusCode) return true;

        logger.LogWarning("Core cevap bildirimi {ResponseId} için {Status} döndü; yeniden denenecek", change.ResponseId, (int)response.StatusCode);
        return false;
    }
}
