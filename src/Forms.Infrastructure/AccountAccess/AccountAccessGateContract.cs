using System.Security.Cryptography;
using System.Text;

namespace Skylab.Forms.Infrastructure.AccountAccess;

public static class AccountAccessGateContract
{
    public const string RedisConnectionName = "account-access-gate";
    public const string ExactIssuer = "https://e.yildizskylab.com/realms/e-skylab";
    public const string ContractKey = "skylab:account-access:v1:contract";
    public const string ContractValue = "sha256(iss\\0sub);marker=1;ttl=none";
    public const string MarkerPrefix = "skylab:account-access:v1:blocked:";
    public const string MarkerValue = "1";
    public const string GoldenSubject = "11111111-1111-1111-1111-111111111111";
    public const string GoldenDigest = "0be50f44b14aa5ca5ae10cfedf7e4986d4d56ddf17b9055e99f3fedcbdb880ff";

    public static string DigestSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var payload = Encoding.UTF8.GetBytes(string.Concat(ExactIssuer, "\0", subject));
        return Convert.ToHexStringLower(SHA256.HashData(payload));
    }

    public static string MarkerKey(string subject) => MarkerPrefix + DigestSubject(subject);
}
