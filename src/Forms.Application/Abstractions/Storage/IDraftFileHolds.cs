namespace Skylab.Forms.Application.Abstractions.Storage;

/// <summary>
/// Taslaktaki cevap dosyalarını core'da taslağa bağlı tutar. Core hiçbir kayda bağlı olmayan
/// answer_file'ı yüklendikten 24 saat sonra siler, taslak ise bir hafta yaşar. Bağları bir arka plan
/// işi core'a işler; burada yalnız istenen durum yazılır.
/// </summary>
public interface IDraftFileHolds
{
    /// <summary>Taslak artık tam olarak bu dosyaları tutar; listede olmayanların bağı kaldırılır.</summary>
    Task HoldAsync(Guid formId, Guid userId, IReadOnlyCollection<Guid> mediaIds, CancellationToken ct = default);

    /// <summary>Formun bütün taslakları silindi.</summary>
    Task ReleaseFormAsync(Guid formId, CancellationToken ct = default);
}
