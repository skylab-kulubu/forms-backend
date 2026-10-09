namespace Skylab.Forms.Application.Contracts.AccountErasure;

/// <summary>Core'un hesap silme komutu; adresler core'un doğruladığı, küçük harfli en çok üç adres.</summary>
public sealed record AccountErasureCommand(Guid RequestId, Guid SubjectId, IReadOnlyList<string> Emails);
