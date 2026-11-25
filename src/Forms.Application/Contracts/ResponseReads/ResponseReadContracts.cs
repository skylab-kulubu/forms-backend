using System.Globalization;

namespace Skylab.Forms.Application.Contracts.ResponseReads;

/// <summary>
/// Core'un okuduğu form: başlık, sorular ve formun ekibi. Cevap eşlemesi soruların Id'siyle yapılır; core eşlemeyi
/// yalnız ekipteki birine kaydettirir. Ekip yalnız kullanıcı kimliği ve roldür.
/// </summary>
public sealed record FormReadContract(Guid FormId, string Title, IReadOnlyList<QuestionReadContract> Questions, IReadOnlyList<CollaboratorReadContract> Collaborators);

public sealed record CollaboratorReadContract(Guid UserId, string Role);

public sealed record QuestionReadContract(string Id, string Type, string Question, string? InputType, string? Identity);

/// <summary>
/// Core'un okuduğu cevap. Status accepted, pending ya da declined; Version son değişim anı (listede geçici cevabın
/// kabulü dahil). Answers yalnız istenen soruların cevaplarıdır; Guest yalnız tek cevap okumasında gelir.
/// </summary>
public sealed record ResponseReadContract(
    Guid FormId,
    Guid ResponseId,
    string Status,
    Guid? UserId,
    bool HasGuest,
    DateTime SubmittedAt,
    DateTime Version,
    IReadOnlyDictionary<string, string>? Answers,
    GuestReadContract? Guest = null
);

public sealed record GuestReadContract(string FirstName, string LastName, string Email);

/// <summary>NextCursor son sayfada null; Watermark yalnız son sayfada dolu: liste o ana kadar eksiksizdir.</summary>
public sealed record ResponseReadPage(IReadOnlyList<ResponseReadContract> Items, string? NextCursor, DateTime? Watermark);

/// <summary>Cevap listesi isteği; Fields cevabı istenen soruların Id'leri, boşsa cevap gitmez.</summary>
public sealed record ResponseReadQuery(bool AcceptedOnly, DateTime? ChangedSince, ResponseReadCursor? After, int Limit, IReadOnlyList<string> Fields);

/// <summary>Bir sayfanın son cevabı: değişim anı ve Id. Core için opaktır.</summary>
public sealed record ResponseReadCursor(DateTime ChangedAt, Guid Id)
{
    public override string ToString() =>
        $"{ChangedAt.Ticks.ToString(CultureInfo.InvariantCulture)}_{Id:N}";

    public static ResponseReadCursor? Parse(string? value)
    {
        var parts = value?.Split('_');
        if (parts is not { Length: 2 } ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
            ticks > DateTime.MaxValue.Ticks ||
            !Guid.TryParseExact(parts[1], "N", out var id))
            return null;

        return new ResponseReadCursor(new DateTime(ticks, DateTimeKind.Utc), id);
    }
}
