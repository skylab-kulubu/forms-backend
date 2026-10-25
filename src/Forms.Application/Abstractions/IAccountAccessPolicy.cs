namespace Skylab.Forms.Application.Abstractions;

public interface IAccountAccessPolicy
{
    Task<AccountAccessDecision> CheckAsync(string issuer, string subject, CancellationToken ct = default);

    Task<bool> IsReadyAsync(CancellationToken ct = default);

    int RetryAfterSeconds { get; }
}

public enum AccountAccessDecision
{
    Allowed,
    Blocked,
    Unavailable
}
