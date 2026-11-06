using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace AuctionSystem.Shared.EventBus;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "auction.events";
    public string QueueName { get; set; } = "auction_default_queue";
}

public class RabbitMqEventBus : IEventBus, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RabbitMqEventBus> _logger;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly Dictionary<string, List<Type>> _handlers = new();

    public RabbitMqEventBus(
        RabbitMqOptions options,
        IServiceProvider serviceProvider,
        ILogger<RabbitMqEventBus> logger)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _logger = logger;

        InitializeRabbitMq();
    }

    private void InitializeRabbitMq()
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.ExchangeDeclare(
            exchange: _options.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);

        _channel.QueueDeclare(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceivedAsync;

        _channel.BasicConsume(
            queue: _options.QueueName,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation(
            "[RabbitMQ] Connected to {Host}:{Port}, exchange '{Exchange}', queue '{Queue}'",
            _options.HostName, _options.Port, _options.ExchangeName, _options.QueueName);
    }

    public Task PublishAsync<TEvent>(TEvent @event, string? routingKey = null) where TEvent : class
    {
        if (_channel == null || _channel.IsClosed)
        {
            throw new InvalidOperationException("RabbitMQ channel is not open.");
        }

        var eventName = typeof(TEvent).Name;
        var rKey = routingKey ?? $"auction.{eventName.ToLowerInvariant()}";

        var json = JsonSerializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.Type = eventName;

        _channel.BasicPublish(
            exchange: _options.ExchangeName,
            routingKey: rKey,
            basicProperties: properties,
            body: body);

        _logger.LogInformation("[RabbitMQ Published] RoutingKey: {RoutingKey}, Event: {EventName}", rKey, eventName);
        return Task.CompletedTask;
    }

    public void Subscribe<TEvent, THandler>() 
        where TEvent : class 
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = typeof(TEvent).Name;
        var handlerType = typeof(THandler);

        if (!_handlers.ContainsKey(eventName))
        {
            _handlers[eventName] = new List<Type>();

            var routingKey = $"auction.{eventName.ToLowerInvariant()}";
            _channel?.QueueBind(
                queue: _options.QueueName,
                exchange: _options.ExchangeName,
                routingKey: routingKey);

            _logger.LogInformation("[RabbitMQ Subscribed] Bound Queue '{Queue}' to RoutingKey '{RoutingKey}'", 
                _options.QueueName, routingKey);
        }

        if (!_handlers[eventName].Contains(handlerType))
        {
            _handlers[eventName].Add(handlerType);
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var eventName = ea.BasicProperties.Type;
        var body = Encoding.UTF8.GetString(ea.Body.ToArray());

        _logger.LogInformation("[RabbitMQ Received] Event: {EventName}, Payload size: {Length} bytes", eventName, body.Length);

        try
        {
            if (!string.IsNullOrEmpty(eventName) && _handlers.TryGetValue(eventName, out var handlerTypes))
            {
                using var scope = _serviceProvider.CreateScope();
                foreach (var handlerType in handlerTypes)
                {
                    var handler = scope.ServiceProvider.GetService(handlerType);
                    if (handler == null) continue;

                    var eventType = handlerType.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IIntegrationEventHandler<>))?
                        .GetGenericArguments()[0];

                    if (eventType != null)
                    {
                        var @event = JsonSerializer.Deserialize(body, eventType);
                        if (@event != null)
                        {
                            var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<object>.HandleAsync));
                            if (method != null)
                            {
                                var task = (Task?)method.Invoke(handler, new[] { @event, CancellationToken.None });
                                if (task != null)
                                {
                                    await task;
                                }
                            }
                        }
                    }
                }
            }

            _channel?.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RabbitMQ Error] Failed processing event {EventName}", eventName);
            _channel?.BasicNack(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
        _connection?.Close();
        _connection?.Dispose();
    }
}

