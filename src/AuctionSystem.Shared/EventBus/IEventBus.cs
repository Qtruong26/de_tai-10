namespace AuctionSystem.Shared.EventBus;

public interface IIntegrationEventHandler<in TEvent> where TEvent : class
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
}

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, string? routingKey = null) where TEvent : class;
    void Subscribe<TEvent, THandler>() 
        where TEvent : class 
        where THandler : IIntegrationEventHandler<TEvent>;
}

