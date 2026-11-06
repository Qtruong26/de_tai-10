using Microsoft.AspNetCore.SignalR;
using AuctionSystem.AuctionApi.Hubs;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.Events;

namespace AuctionSystem.AuctionApi.EventHandlers;

public class BidAcceptedSignalRHandler : IIntegrationEventHandler<BidAcceptedEvent>
{
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<BidAcceptedSignalRHandler> _logger;

    public BidAcceptedSignalRHandler(IHubContext<AuctionHub> hubContext, ILogger<BidAcceptedSignalRHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task HandleAsync(BidAcceptedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[AuctionHub SignalR Broadcasting] BidAccepted for Auction {AuctionId}: {Bidder} - {Amount:N0} VND",
            @event.AuctionId, @event.BidderName, @event.Amount);

        // Broadcast to clients watching this specific auction
        await _hubContext.Clients.Group(@event.AuctionId).SendAsync("ReceiveBid", @event, cancellationToken);

        // Also broadcast summary to all clients for live dashboard overview
        await _hubContext.Clients.All.SendAsync("AuctionPriceUpdated", new
        {
            @event.AuctionId,
            @event.NewCurrentPrice,
            @event.BidderName,
            @event.Timestamp
        }, cancellationToken);
    }
}

public class AuctionEndedSignalRHandler : IIntegrationEventHandler<AuctionEndedEvent>
{
    private readonly IHubContext<AuctionHub> _hubContext;
    private readonly ILogger<AuctionEndedSignalRHandler> _logger;

    public AuctionEndedSignalRHandler(IHubContext<AuctionHub> hubContext, ILogger<AuctionEndedSignalRHandler> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task HandleAsync(AuctionEndedEvent @event, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[AuctionHub SignalR Broadcasting] AuctionEnded for Auction {AuctionId}. Winner: {Winner}, Price: {Price:N0} VND",
            @event.AuctionId, @event.WinnerName ?? "None", @event.FinalPrice);

        // Broadcast to clients watching this specific auction
        await _hubContext.Clients.Group(@event.AuctionId).SendAsync("AuctionEnded", @event, cancellationToken);

        // Also broadcast to all clients
        await _hubContext.Clients.All.SendAsync("AuctionStatusChanged", new
        {
            @event.AuctionId,
            Status = "Ended",
            @event.WinnerName,
            @event.WinningBid,
            @event.EndedAt
        }, cancellationToken);
    }
}

