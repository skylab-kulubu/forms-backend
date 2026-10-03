namespace Skylab.Forms.Application.Contracts.AccountErasure;

public sealed record AccountErasureCommand(Guid RequestId, Guid SubjectId, IReadOnlyList<string> Emails);
