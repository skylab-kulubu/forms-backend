using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Infrastructure.Outbox;

namespace Skylab.Forms.Infrastructure.Integrations.RabbitMq;

/// <summary>
/// Ayarlar kök düzeyde, öbür servislerin RabbitMQ ayarlarıyla aynı adlarla okunur; dağıtımın gece parola dönüşü
/// login'i bu adlardan bulur.
/// </summary>
internal sealed class RabbitMqOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public static class RabbitMqRegistration
{
    public static IServiceCollection AddRabbitMq(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["RABBITMQ_HOST"]))
        {
            services.AddSingleton(new IntegrationNotice(LogLevel.Warning,
                "RABBITMQ_HOST boş: RabbitMQ kapalı; olaylar yayımlanmıyor, giden kutusunda yedi gün bekliyor"));

            return services;
        }

        services.AddOptions<RabbitMqOptions>()
            .Configure<IConfiguration>((rabbit, config) =>
            {
                rabbit.Host = config["RABBITMQ_HOST"]?.Trim() ?? "";
                rabbit.Port = config.GetValue("RABBITMQ_PORT", 5672);
                rabbit.VirtualHost = config["RABBITMQ_VHOST"]?.Trim() ?? "";
                rabbit.Username = config["RABBITMQ_USERNAME"]?.Trim() ?? "";
                rabbit.Password = config["RABBITMQ_PASSWORD"] ?? "";
            })
            .Validate(rabbit => rabbit.Port is > 0 and <= ushort.MaxValue, "RABBITMQ_PORT must be the broker's AMQP port, like 5672.")
            .Validate(rabbit => rabbit.VirtualHost.Length > 0, "RABBITMQ_VHOST must name the virtual host Forms publishes in.")
            .Validate(rabbit => rabbit.Username.Length > 0 && rabbit.Password.Length > 0, "RABBITMQ_USERNAME and RABBITMQ_PASSWORD must hold Forms' RabbitMQ login while RABBITMQ_HOST is set.")
            .ValidateOnStart();

        services.AddSingleton<IOutboxHandler, RabbitMqEventPublisher>();

        return services;
    }
}
