using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Skylab.Forms.Api.Extensions;

public sealed class FrontendCorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>API'yi tarayıcıdan çağıran ön yüzün kökeni. Boşsa başka kökenden gelen çağrıya izin verilmez.</summary>
    public string AllowedOrigin { get; init; } = "";
}

public static class FrontendCorsExtensions
{
    public const string PolicyName = "AllowFrontend";

    public static IServiceCollection AddFrontendCors(this IServiceCollection services)
    {
        services.AddOptions<FrontendCorsOptions>()
            .BindConfiguration(FrontendCorsOptions.SectionName)
            .Validate(
                cors => cors.AllowedOrigin.Length == 0 || IsOrigin(cors.AllowedOrigin),
                "Cors:AllowedOrigin must be a scheme, host and optional port without a path, like https://forms.example.org.")
            .ValidateOnStart();

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<FrontendCorsOptions>>((options, frontend) =>
            options.AddPolicy(PolicyName, policy =>
            {
                if (frontend.Value.AllowedOrigin.Length > 0)
                    policy.WithOrigins(frontend.Value.AllowedOrigin).AllowAnyHeader().AllowAnyMethod();
            }));

        return services;
    }

    // Tarayıcının gönderdiği Origin yolsuzdur; sonundaki bir "/" bile eşleşmeyi bozar.
    private static bool IsOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var origin)
        && origin.Scheme is "https" or "http"
        && origin.UserInfo.Length == 0
        && value.TrimEnd('/') == value
        && origin.AbsolutePath == "/"
        && origin.Query.Length == 0
        && origin.Fragment.Length == 0;
}
