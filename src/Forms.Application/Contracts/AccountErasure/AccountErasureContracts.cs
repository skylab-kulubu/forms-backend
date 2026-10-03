using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Contracts.AccountErasure;

/// <summary>
/// Core'un hesap silme komutu. Kişinin kimliği ve adresleri yalnız bu nesnede durur:
/// loga, hata mesajına ya da kalıcı bir depoya yazılmaz.
/// </summary>
/// <param name="Emails">0–3 adres; kırpılmış ve küçük harfe çevrilmiş.</param>
public sealed record AccountErasureCommand(Guid RequestId, Guid SubjectId, IReadOnlyList<string> Emails);

/// <summary>Tamamlanmış bir silmenin makbuzu; tekrar gelen komuta da aynen döner.</summary>
public sealed record AccountErasureReceiptContract(
    Guid RequestId,
    DateTime CompletedAt,
    IReadOnlyDictionary<string, long> Counts);

/// <summary>Kişiye ait bir yanıt ve formunun şeması: kişinin adını yanıttan okumak için.</summary>
public sealed record AccountErasureSubjectResponse(
    Guid ResponseId,
    IReadOnlyList<FormSchemaItem> Schema,
    IReadOnlyList<FormResponseSchemaItem> Answers);

/// <summary>Veritabanındaki silmeye girdi: komut ve kişi için bulunan tam adlar.</summary>
/// <param name="Names">Serbest metinde aranacak, en az iki kelimelik tam adlar.</param>
/// <param name="CacheCounts">Transaction'dan önce Redis'te yapılan silmelerin sayıları.</param>
public sealed record AccountErasureWork(
    AccountErasureCommand Command,
    IReadOnlyList<string> Names,
    IReadOnlyDictionary<string, long> CacheCounts);
