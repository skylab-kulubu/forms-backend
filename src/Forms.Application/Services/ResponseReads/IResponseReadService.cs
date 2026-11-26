using Skylab.Forms.Application.Contracts.ResponseReads;

namespace Skylab.Forms.Application.Services.ResponseReads;

public interface IResponseReadService
{
    /// <summary>Formun başlığı, soruları ve ekibi; form yoksa ya da silinmişse null.</summary>
    Task<FormReadContract?> GetFormAsync(Guid formId, CancellationToken ct = default);

    /// <summary>Formun cevaplarından bir sayfa; form yoksa ya da silinmişse null.</summary>
    Task<ResponseReadPage?> ListAsync(Guid formId, ResponseReadQuery query, CancellationToken ct = default);

    /// <summary>Formun tek cevabı, misafirin adı ve adresiyle; bu formun değilse, arşivliyse, geçiciyse ya da Silinmiş kullanıcı'nınsa null.</summary>
    Task<ResponseReadContract?> GetAsync(Guid formId, Guid responseId, IReadOnlyList<string> fields, CancellationToken ct = default);
}
