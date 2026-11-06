using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AuctionSystem.Shared.EventBus;

public static class EventBusExtensions
{
    public static IServiceCollection AddAuctionEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        string defaultQueueName)
    {
        var rabbitOptions = new RabbitMqOptions();
        var section = configuration.GetSection("RabbitMQ");
        if (section.Exists())
        {
            section.Bind(rabbitOptions);
        }

        if (string.IsNullOrEmpty(rabbitOptions.QueueName) || rabbitOptions.QueueName == "auction_default_queue")
        {
            rabbitOptions.QueueName = defaultQueueName;
        }

        services.AddSingleton(rabbitOptions);

        services.AddSingleton<IEventBus>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var busLogger = loggerFactory.CreateLogger("AuctionSystem.EventBus");

            bool rabbitAvailable = false;
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = rabbitOptions.HostName,
                    Port = rabbitOptions.Port,
                    UserName = rabbitOptions.UserName,
                    Password = rabbitOptions.Password,
                    VirtualHost = rabbitOptions.VirtualHost,
                    RequestedConnectionTimeout = TimeSpan.FromMilliseconds(1500)
                };

                using var testConn = factory.CreateConnection();
                rabbitAvailable = testConn.IsOpen;
            }
            catch (Exception)
            {
                rabbitAvailable = false;
            }

            if (rabbitAvailable)
            {
                busLogger.LogInformation(
                    "--> RabbitMQ is AVAILABLE at {Host}:{Port}. Initializing RabbitMqEventBus with queue '{Queue}'...",
                    rabbitOptions.HostName, rabbitOptions.Port, rabbitOptions.QueueName);
                var rabbitLogger = sp.GetRequiredService<ILogger<RabbitMqEventBus>>();
                return new RabbitMqEventBus(rabbitOptions, sp, rabbitLogger);
            }
            else
            {
                busLogger.LogWarning(
                    "--> RabbitMQ is NOT reachable at {Host}:{Port}. Automatically activating resilient In-Memory EventBus fallback.",
                    rabbitOptions.HostName, rabbitOptions.Port);
                var memLogger = sp.GetRequiredService<ILogger<InMemoryEventBus>>();
                return new InMemoryEventBus(sp, memLogger);
            }
        });

        return services;
    }
}

