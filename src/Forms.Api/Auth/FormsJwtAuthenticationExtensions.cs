using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.IdentityModel.Tokens;

namespace Skylab.Forms.Api.Auth;

public static class FormsJwtAuthenticationExtensions
{
    public const string ExactIssuer = "https://e.yildizskylab.com/realms/e-skylab";
    public const string ExactSandboxIssuer = "https://e.yildizskylab.com/realms/e-skylab-sandbox";
    public const string ExactAudience = "forms";

    public static IServiceCollection AddFormsJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"] ?? ExactIssuer;
        var audience = configuration["Authentication:Audience"] ?? ExactAudience;
        var clockSkewSeconds = configuration.GetValue("Authentication:ClockSkewSeconds", 30);
        var metadataTimeoutSeconds = configuration.GetValue("Authentication:MetadataTimeoutSeconds", 5);

        if (!IsTrustedIssuer(issuer))
        {
            throw new InvalidOperationException(
                $"Authentication:Issuer must exactly match {ExactIssuer} or {ExactSandboxIssuer}.");
        }

        if (!string.Equals(audience, ExactAudience, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Authentication:Audience must exactly match {ExactAudience}.");
        }

        if (clockSkewSeconds is < 0 or > 120)
        {
            throw new InvalidOperationException(
                "Authentication:ClockSkewSeconds must be between 0 and 120.");
        }

        if (metadataTimeoutSeconds is < 1 or > 30)
        {
            throw new InvalidOperationException(
                "Authentication:MetadataTimeoutSeconds must be between 1 and 30.");
        }

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;
                options.Audience = audience;
                options.RequireHttpsMetadata = true;
                options.MapInboundClaims = false;
                options.BackchannelTimeout = TimeSpan.FromSeconds(metadataTimeoutSeconds);
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    RequireAudience = true,
                    IgnoreTrailingSlashWhenValidatingAudience = false,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(clockSkewSeconds)
                };
            });

        return services;
    }

    private static bool IsTrustedIssuer(string issuer) =>
        string.Equals(issuer, ExactIssuer, StringComparison.Ordinal) ||
        string.Equals(issuer, ExactSandboxIssuer, StringComparison.Ordinal);

    public static IApplicationBuilder UseFormsJwtAuthentication(
        this IApplicationBuilder app,
        string corsPolicyName)
    {
        app.UseAuthentication();

        return app.Use(async (context, next) =>
        {
            var credentialState = GetCredentialState(context.Request);
            if (credentialState == CredentialState.Invalid)
            {
                await ChallengeAsync(context, corsPolicyName);
                return;
            }

            if (credentialState == CredentialState.Bearer)
            {
                var result = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                if (!result.Succeeded)
                {
                    await ChallengeAsync(context, corsPolicyName);
                    return;
                }
            }

            await next(context);
        });
    }

    private static async Task ChallengeAsync(HttpContext context, string corsPolicyName)
    {
        var policyProvider = context.RequestServices.GetRequiredService<ICorsPolicyProvider>();
        var corsService = context.RequestServices.GetRequiredService<ICorsService>();
        var policy = await policyProvider.GetPolicyAsync(context, corsPolicyName);

        if (policy is not null)
        {
            var corsResult = corsService.EvaluatePolicy(context, policy);
            corsService.ApplyResult(corsResult, context.Response);
        }

        await context.ChallengeAsync(JwtBearerDefaults.AuthenticationScheme);
    }

    internal static CredentialState GetCredentialState(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var values))
            return CredentialState.None;

        if (values.Count != 1 || values[0] is not { } value)
            return CredentialState.Invalid;

        const string prefix = "Bearer ";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return CredentialState.Invalid;

        var token = value.AsSpan(prefix.Length);
        if (token.IsEmpty)
            return CredentialState.Invalid;

        foreach (var character in token)
        {
            if (char.IsWhiteSpace(character) || character == ',')
                return CredentialState.Invalid;
        }

        return CredentialState.Bearer;
    }

    internal enum CredentialState
    {
        None,
        Bearer,
        Invalid
    }
}
