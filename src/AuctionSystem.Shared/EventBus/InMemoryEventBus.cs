using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuctionSystem.Shared.EventBus;

public class InMemoryEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InMemoryEventBus> _logger;
    private readonly ConcurrentDictionary<string, List<Type>> _handlers = new();

    public InMemoryEventBus(IServiceProvider serviceProvider, ILogger<InMemoryEventBus> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, string? routingKey = null) where TEvent : class
    {
        var eventName = typeof(TEvent).Name;
        _logger.LogInformation("[EventBus Published] Event: {EventName}, RoutingKey: {RoutingKey}", eventName, routingKey);

        if (_handlers.TryGetValue(eventName, out var handlerTypes))
        {
            var json = JsonSerializer.Serialize(@event);
            foreach (var handlerType in handlerTypes.ToList())
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var handler = scope.ServiceProvider.GetService(handlerType);
                        if (handler != null)
                        {
                            var deserialized = JsonSerializer.Deserialize<TEvent>(json);
                            if (deserialized != null)
                            {
                                var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<TEvent>.HandleAsync));
                                if (method != null)
                                {
                                    var task = (Task?)method.Invoke(handler, new object[] { deserialized, CancellationToken.None });
                                    if (task != null)
                                    {
                                        await task;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[EventBus Error] Exception handling event {EventName}", eventName);
                    }
                });
            }
        }

        await Task.CompletedTask;
    }

    public void Subscribe<TEvent, THandler>() 
        where TEvent : class 
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = typeof(TEvent).Name;
        var handlerType = typeof(THandler);

        _handlers.AddOrUpdate(
            eventName,
            new List<Type> { handlerType },
            (_, list) =>
            {
                lock (list)
                {
                    if (!list.Contains(handlerType))
                    {
                        list.Add(handlerType);
                    }
                }
                return list;
            });

        _logger.LogInformation("[EventBus Subscribed] {Handler} subscribed to {EventName}", handlerType.Name, eventName);
    }
}

