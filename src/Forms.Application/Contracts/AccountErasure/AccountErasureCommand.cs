namespace Skylab.Forms.Application.Contracts.AccountErasure;

/// <summary>Core'un hesap silme komutu; adresler küçük harfli, en çok üç tane.</summary>
public sealed record AccountErasureCommand(Guid RequestId, Guid SubjectId, IReadOnlyList<string> Emails);
