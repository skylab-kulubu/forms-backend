using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.AccountAccess;

public enum AccountAccessDecision
{
    Allowed,
    Blocked,
    Unavailable
}

/// <summary>
/// Core'un tek yazarı olduğu kalıcı hesap engeli listesini okur (core: docs/account-access-gate.md).
/// Anahtar biçimi, sözleşme değeri ve hata anında kapalı kalma core ile birebir aynı kalmalı.
/// </summary>
public sealed class AccountAccessGate(IConnectionMultiplexer connection, AccountAccessGateOptions options) : IDisposable
{
    private const string Issuer = "https://e.yildizskylab.com/realms/e-skylab";
    private const string ContractKey = "skylab:account-access:v1:contract";
    private const string ContractValue = "sha256(iss\\0sub);marker=1;ttl=none";
    private const string MarkerPrefix = "skylab:account-access:v1:blocked:";
    private const string MarkerValue = "1";

    public async Task<AccountAccessDecision> CheckSubjectAsync(string subject, CancellationToken cancellationToken)
    {
        RedisKey[] keys = [ContractKey, MarkerKey(subject)];

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

    public async Task<bool> IsContractReadyAsync(CancellationToken cancellationToken)
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
        read(connection.GetDatabase(options.Database))
            .WaitAsync(TimeSpan.FromMilliseconds(options.OperationTimeoutMilliseconds), cancellationToken);

    // Redis'e subject değil, yalnız sha256(issuer + NUL + subject) yazılır.
    private static RedisKey MarkerKey(string subject) =>
        MarkerPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Issuer}\0{subject}")));

    private static bool Matches(RedisValue value, string expected) =>
        value.HasValue && string.Equals(value.ToString(), expected, StringComparison.Ordinal);

    private static bool IsOutage(Exception ex, CancellationToken cancellationToken) =>
        ex is TimeoutException or RedisException or ObjectDisposedException
        || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}

public static class AccountAccessGateRegistration
{
    /// <summary>off modunda kapı hiç kaydedilmez; middleware ve hazırlık ucu bunu kapalı kapı sayar.</summary>
    public static IServiceCollection AddAccountAccessGate(this IServiceCollection services, IConfiguration configuration)
    {
        var options = AccountAccessGateOptions.FromConfiguration(configuration);
        services.AddSingleton(options);

        if (options.Mode == AccountAccessGateMode.Enforce)
            services.AddSingleton(_ => new AccountAccessGate(ConnectionMultiplexer.Connect(options.ToRedisConfiguration()), options));

        return services;
    }
}
