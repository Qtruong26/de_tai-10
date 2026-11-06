using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.EventHandlers;
using AuctionSystem.Shared.Events;

namespace AuctionSystem.AuditWorker;

public class Worker : BackgroundService
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<Worker> _logger;

    public Worker(IEventBus eventBus, ILogger<Worker> logger)
    {
        _eventBus = eventBus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("==================================================================");
        _logger.LogInformation(" [AUDIT WORKER] IS LISTENING TO RABBITMQ EVENTS...");
        _logger.LogInformation(" -> Subscribed to: BidAcceptedEvent, AuctionEndedEvent");
        _logger.LogInformation(" -> Queue:         audit_worker_queue");
        _logger.LogInformation("==================================================================");

        // Subscribing handlers to RabbitMQ / EventBus
        _eventBus.Subscribe<BidAcceptedEvent, AuditBidAcceptedHandler>();
        _eventBus.Subscribe<AuctionEndedEvent, AuditAuctionEndedHandler>();

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
        }
    }
}
