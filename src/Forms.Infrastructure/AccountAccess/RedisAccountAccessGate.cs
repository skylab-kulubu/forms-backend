using StackExchange.Redis;

namespace Skylab.Forms.Infrastructure.AccountAccess;

internal sealed class RedisAccountAccessGate(
    IConnectionMultiplexer connection,
    AccountAccessGateOptions options) : IAccountAccessGate
{
    public AccountAccessGateMode Mode => options.Mode;

    public async Task<AccountAccessDecision> CheckSubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        if (Mode == AccountAccessGateMode.Off)
            return AccountAccessDecision.Allowed;

        RedisKey[] keys =
        [
            AccountAccessGateContract.ContractKey,
            AccountAccessGateContract.MarkerKey(subject)
        ];

        try
        {
            var values = await ExecuteBoundedAsync(
                database => database.StringGetAsync(keys, CommandFlags.DemandMaster),
                cancellationToken);

            if (values.Length != 2 ||
                !values[0].HasValue ||
                !string.Equals(values[0].ToString(), AccountAccessGateContract.ContractValue, StringComparison.Ordinal))
            {
                return AccountAccessDecision.Unavailable;
            }

            if (!values[1].HasValue)
                return AccountAccessDecision.Allowed;

            return string.Equals(
                values[1].ToString(),
                AccountAccessGateContract.MarkerValue,
                StringComparison.Ordinal)
                ? AccountAccessDecision.Blocked
                : AccountAccessDecision.Unavailable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AccountAccessDecision.Unavailable;
        }
        catch (TimeoutException)
        {
            return AccountAccessDecision.Unavailable;
        }
        catch (RedisException)
        {
            return AccountAccessDecision.Unavailable;
        }
        catch (ObjectDisposedException)
        {
            return AccountAccessDecision.Unavailable;
        }
    }

    public async Task<bool> IsContractReadyAsync(CancellationToken cancellationToken = default)
    {
        if (Mode == AccountAccessGateMode.Off)
            return true;

        try
        {
            var value = await ExecuteBoundedAsync(
                database => database.StringGetAsync(
                    AccountAccessGateContract.ContractKey,
                    CommandFlags.DemandMaster),
                cancellationToken);

            return value.HasValue && string.Equals(
                value.ToString(),
                AccountAccessGateContract.ContractValue,
                StringComparison.Ordinal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (RedisException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private async Task<T> ExecuteBoundedAsync<T>(
        Func<IDatabase, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var database = connection.GetDatabase(options.Database);
        return await operation(database).WaitAsync(
            TimeSpan.FromMilliseconds(options.OperationTimeoutMilliseconds),
            cancellationToken);
    }
}
