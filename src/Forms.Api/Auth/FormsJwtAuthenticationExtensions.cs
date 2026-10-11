using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Skylab.Forms.Api.Auth;

public static class FormsJwtAuthenticationExtensions
{
    public static IServiceCollection AddFormsJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Authentication:Issuer"] ?? "https://e.yildizskylab.com/realms/e-skylab";
                options.Audience = configuration["Authentication:Audience"] ?? "forms";
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
