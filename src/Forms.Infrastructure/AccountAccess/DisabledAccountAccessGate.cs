namespace Skylab.Forms.Infrastructure.AccountAccess;

internal sealed class DisabledAccountAccessGate : IAccountAccessGate
{
    public AccountAccessGateMode Mode => AccountAccessGateMode.Off;

    public Task<AccountAccessDecision> CheckSubjectAsync(
        string subject,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AccountAccessDecision.Allowed);

    public Task<bool> IsContractReadyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
