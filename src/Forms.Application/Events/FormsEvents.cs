using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Events;

/// <summary>
/// forms.events'e yayımlanan olayların türleri. Olay yalnız kimlik ve karar taşır: misafirin adı ve adresi, cevaplar
/// ve notlar olaya girmez; gerekeni dinleyen iç okuma uçlarından okur.
/// </summary>
public static class FormsEvents
{
    public const string ResponseCreated = "forms.response.created";
    public const string ResponseStatusChanged = "forms.response.status_changed";
    public const string FormUpdated = "forms.form.updated";

    /// <summary>Core'a giden cevap bildirimiyle aynı eşleme; geçici cevabın olayı yok.</summary>
    public static string? StatusOf(FormResponseStatus status) => status switch
    {
        FormResponseStatus.NonRestrict or FormResponseStatus.Approved => "accepted",
        FormResponseStatus.Pending or FormResponseStatus.Flagged => "pending",
        FormResponseStatus.Declined or FormResponseStatus.Returned => "declined",
        _ => null
    };
}

/// <summary>Cevap ilk kez teslim sayıldı: yeni gönderim ya da ekibin kabul ettiği geçici cevap.</summary>
public sealed record ResponseCreatedData(
    Guid FormId,
    Guid ResponseId,
    string Status,
    Guid? UserId,
    bool HasGuest,
    DateTime SubmittedAt,
    IReadOnlyDictionary<string, string> HiddenFields,
    DateTime Version)
{
    // Formlarda gizli alan henüz yok; geldiğinde yalnız kısa kodlar ([A-Za-z0-9_-]{1,100}) olaya girer.
    private static readonly IReadOnlyDictionary<string, string> NoHiddenFields = new Dictionary<string, string>();

    // Adsız misafir sayılmaz: core misafir biletini ad ve soyadsız yazmaz.
    public static ResponseCreatedData Of(FormResponse response, string status, DateTime version) =>
        new(response.FormId, response.Id, status, response.UserId, response.Guest is { HasName: true }, response.SubmittedAt, NoHiddenFields, version);
}

public sealed record ResponseStatusChangedData(Guid FormId, Guid ResponseId, string Status, Guid? UserId, DateTime Version)
{
    public static ResponseStatusChangedData Of(FormResponse response, string status, DateTime version) =>
        new(response.FormId, response.Id, status, response.UserId, version);
}

/// <summary>Formun durumu (open, closed, deleted) ya da kapanış anı değişti.</summary>
public sealed record FormUpdatedData(Guid FormId, string Status, DateTime? ClosesAt, DateTime Version)
{
    public static FormUpdatedData Of(Form form, DateTime version) =>
        new(form.Id, form.Status.ToString().ToLowerInvariant(), form.ClosesAt, version);
}
