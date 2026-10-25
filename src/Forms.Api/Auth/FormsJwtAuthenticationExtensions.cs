using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Skylab.Forms.Api.Auth;

public sealed class FormsAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Token'ı veren kimlik sağlayıcının adresi; imza anahtarları bu adresin keşif belgesinden okunur.</summary>
    public string Issuer { get; init; } = "";

    public string Audience { get; init; } = "";
}

public static class FormsJwtAuthenticationExtensions
{
    public static IServiceCollection AddFormsJwtAuthentication(this IServiceCollection services)
    {
        services.AddOptions<FormsAuthenticationOptions>()
            .BindConfiguration(FormsAuthenticationOptions.SectionName)
            .Validate(
                settings => Uri.TryCreate(settings.Issuer, UriKind.Absolute, out var issuer) && issuer.Scheme is "https" or "http",
                "Authentication:Issuer must be the address of the token issuer, like https://auth.example.org/realms/forms.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "Authentication:Audience must name the audience Forms' tokens carry.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<FormsAuthenticationOptions>>((options, settings) =>
            {
                options.Authority = settings.Value.Issuer;
                options.Audience = settings.Value.Audience;
                options.MapInboundClaims = false;
                options.BackchannelTimeout = TimeSpan.FromSeconds(5);
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
                options.TokenValidationParameters.ValidateIssuerSigningKey = true;
                options.TokenValidationParameters.IgnoreTrailingSlashWhenValidatingAudience = false;
            });

        return services;
    }

    /// <summary>
    /// Gönderilmiş ama doğrulanamayan bir token anonim sayılmaz, 401 döner: oturumu düşen
    /// istemci yeniden giriş yapsın, cevabı da anonim kaydedilmesin.
    /// </summary>
    public static IApplicationBuilder UseFormsJwtAuthentication(this IApplicationBuilder app) =>
        app.UseAuthentication().Use(async (context, next) =>
        {
            if (context.Request.Headers.Authorization.Count > 0 && context.User.Identity?.IsAuthenticated != true)
            {
                await context.ChallengeAsync();
                return;
            }

            await next(context);
        });
}
