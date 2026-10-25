using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Skylab.Forms.Tests.Integration;

/// <summary>Keycloak biçiminde, testlerin kendi imzaladığı token'lar; API'nin JWT hattı olduğu gibi çalışır.</summary>
internal static class TestTokens
{
    public const string Issuer = "https://auth.forms.test/realms/forms";
    public const string Audience = "forms";

    public static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("forms-tests-only-signing-key-0123456789"));

    public static string For(Guid userId, params string[] formsRoles) =>
        Create(userId, SigningKey, DateTime.UtcNow.AddHours(1), formsRoles);

    public static string Create(Guid userId, SecurityKey key, DateTime expires, params string[] formsRoles) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = expires.AddHours(-2),
            NotBefore = expires.AddHours(-2),
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId.ToString(),
                ["resource_access"] = new Dictionary<string, object>
                {
                    ["forms"] = new Dictionary<string, object> { ["roles"] = formsRoles }
                }
            }
        });
}
