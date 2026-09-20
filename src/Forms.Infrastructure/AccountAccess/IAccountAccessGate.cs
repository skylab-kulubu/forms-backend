namespace Skylab.Forms.Infrastructure.AccountAccess;

public enum AccountAccessDecision
{
    Allowed,
    Blocked,
    Unavailable
}

public interface IAccountAccessGate
{
    AccountAccessGateMode Mode { get; }

    Task<AccountAccessDecision> CheckSubjectAsync(
        string subject,
        CancellationToken cancellationToken = default);

    Task<bool> IsContractReadyAsync(CancellationToken cancellationToken = default);
}
