using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.Events;
using AuctionSystem.Shared.Models;

namespace AuctionSystem.Shared.EventHandlers;

public class AuditBidAcceptedHandler : IIntegrationEventHandler<BidAcceptedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditBidAcceptedHandler> _logger;

    public AuditBidAcceptedHandler(IServiceProvider serviceProvider, ILogger<AuditBidAcceptedHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task HandleAsync(BidAcceptedEvent @event, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            AuctionId = @event.AuctionId,
            EventType = "BidAccepted",
            BidderName = @event.BidderName,
            Amount = @event.Amount,
            Details = $"Lượt trả giá hợp lệ: '{@event.BidderName}' đã trả {@event.Amount:N0} VND. Giá sàn mới: {@event.NewCurrentPrice:N0} VND.",
            Timestamp = @event.Timestamp
        };

        db.AuditLogs.Add(auditLog);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Audit Handler] Ghi nhật ký BidAccepted cho phiên {AuctionId}: {Bidder} - {Amount:N0} VND",
            @event.AuctionId, @event.BidderName, @event.Amount);
    }
}

public class AuditAuctionEndedHandler : IIntegrationEventHandler<AuctionEndedEvent>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditAuctionEndedHandler> _logger;

    public AuditAuctionEndedHandler(IServiceProvider serviceProvider, ILogger<AuditAuctionEndedHandler> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task HandleAsync(AuctionEndedEvent @event, CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();

        var details = @event.HasWinner
            ? $"Phiên đấu giá '{@event.Title}' đã kết thúc. Người chiến thắng: '{@event.WinnerName}' với mức giá thắng: {@event.WinningBid:N0} VND (Giá chốt: {@event.FinalPrice:N0} VND)."
            : $"Phiên đấu giá '{@event.Title}' đã kết thúc mà không có người trả giá nào.";

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid().ToString(),
            AuctionId = @event.AuctionId,
            EventType = "AuctionEnded",
            BidderName = @event.WinnerName,
            Amount = @event.WinningBid,
            Details = details,
            Timestamp = @event.EndedAt
        };

        db.AuditLogs.Add(auditLog);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Audit Handler] Ghi nhật ký AuctionEnded cho phiên {AuctionId}: Winner: {Winner}, FinalPrice: {Price:N0} VND",
            @event.AuctionId, @event.WinnerName ?? "N/A", @event.FinalPrice);
    }
}

