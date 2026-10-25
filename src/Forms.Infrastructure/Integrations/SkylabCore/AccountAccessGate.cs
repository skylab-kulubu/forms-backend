using System.Security.Cryptography;
using System.Text;
using Skylab.Forms.Application.Abstractions;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.Integrations.SkylabCore;

/// <summary>
/// Core'un tek yazarı olduğu kalıcı hesap engeli listesini okur (core: docs/account-access-gate.md).
/// Anahtar biçimi, sözleşme değeri ve hata anında kapalı kalma core ile birebir aynı kalmalı.
/// </summary>
internal sealed class AccountAccessGate(IConnectionMultiplexer connection, AccountAccessGateOptions options) : IAccountAccessPolicy, IDisposable
{
    private const string ContractKey = "skylab:account-access:v1:contract";
    private const string ContractValue = "sha256(iss\\0sub);marker=1;ttl=none";
    private const string MarkerPrefix = "skylab:account-access:v1:blocked:";
    private const string MarkerValue = "1";

    public async Task<AccountAccessDecision> CheckAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        RedisKey[] keys = [ContractKey, MarkerKey(issuer, subject)];

        try
        {
            var values = await ReadAsync(database => database.StringGetAsync(keys, CommandFlags.DemandMaster), cancellationToken);

            if (values.Length != 2 || !Matches(values[0], ContractValue)) return AccountAccessDecision.Unavailable;
            if (values[1].IsNull) return AccountAccessDecision.Allowed;

            return Matches(values[1], MarkerValue) ? AccountAccessDecision.Blocked : AccountAccessDecision.Unavailable;
        }
        catch (Exception ex) when (IsOutage(ex, cancellationToken))
        {
            return AccountAccessDecision.Unavailable;
        }
    }

    public int RetryAfterSeconds => options.RetryAfter;

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var value = await ReadAsync(database => database.StringGetAsync(ContractKey, CommandFlags.DemandMaster), cancellationToken);

            return Matches(value, ContractValue);
        }
        catch (Exception ex) when (IsOutage(ex, cancellationToken))
        {
            return false;
        }
    }

    public void Dispose() => connection.Dispose();

    private Task<T> ReadAsync<T>(Func<IDatabase, Task<T>> read, CancellationToken cancellationToken) =>
        read(connection.GetDatabase(options.Database ?? 0))
            .WaitAsync(TimeSpan.FromMilliseconds(options.OperationTimeout), cancellationToken);

    // Redis'e subject değil, yalnız sha256(issuer + NUL + subject) yazılır.
    private static RedisKey MarkerKey(string issuer, string subject) =>
        MarkerPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer}\0{subject}")));

    private static bool Matches(RedisValue value, string expected) =>
        value.HasValue && string.Equals(value.ToString(), expected, StringComparison.Ordinal);

    private static bool IsOutage(Exception ex, CancellationToken cancellationToken) =>
        ex is TimeoutException or RedisException or ObjectDisposedException
        || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
