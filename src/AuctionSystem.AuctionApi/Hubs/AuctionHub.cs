using Microsoft.AspNetCore.SignalR;

namespace AuctionSystem.AuctionApi.Hubs;

public class AuctionHub : Hub
{
    private readonly ILogger<AuctionHub> _logger;

    public AuctionHub(ILogger<AuctionHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinAuction(string auctionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, auctionId);
        _logger.LogInformation("[SignalR] Connection {ConnectionId} joined Auction Group {AuctionId}", Context.ConnectionId, auctionId);
        await Clients.Caller.SendAsync("JoinedGroup", new { auctionId, message = $"Đã tham gia theo dõi phiên đấu giá {auctionId}" });
    }

    public async Task LeaveAuction(string auctionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, auctionId);
        _logger.LogInformation("[SignalR] Connection {ConnectionId} left Auction Group {AuctionId}", Context.ConnectionId, auctionId);
        await Clients.Caller.SendAsync("LeftGroup", new { auctionId, message = $"Đã rời khỏi phiên đấu giá {auctionId}" });
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("[SignalR] Client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[SignalR] Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

