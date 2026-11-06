using Microsoft.EntityFrameworkCore;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.Events;
using AuctionSystem.Shared.Models;

namespace AuctionSystem.AuctionApi.BackgroundServices;

public class AuctionExpirationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuctionExpirationWorker> _logger;

    public AuctionExpirationWorker(
        IServiceProvider serviceProvider,
        ILogger<AuctionExpirationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[AuctionExpirationWorker] Background timer started. Checking every 3s...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiredAuctionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuctionExpirationWorker] Error checking expired auctions.");
            }

            await Task.Delay(3000, stoppingToken);
        }
    }

    private async Task CheckExpiredAuctionsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var now = DateTime.UtcNow;

        // Find active auctions where EndTime <= now
        var expiredAuctions = await db.Auctions
            .Include(a => a.Bids)
            .Where(a => a.Status == AuctionStatus.Active && a.EndTime <= now)
            .ToListAsync(ct);

        foreach (var auction in expiredAuctions)
        {
            auction.Status = AuctionStatus.Ended;

            // Determine winner
            var highestBid = auction.Bids
                .OrderByDescending(b => b.Amount)
                .ThenBy(b => b.Timestamp)
                .FirstOrDefault();

            if (highestBid != null)
            {
                auction.WinnerName = highestBid.BidderName;
                auction.WinningBid = highestBid.Amount;
                highestBid.IsWinning = true;
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[Auction Expired & Ended] Auction '{Title}' (ID: {Id}) ended! Winner: {Winner}, WinningBid: {Amount:N0} VND",
                auction.Title, auction.Id, auction.WinnerName ?? "None", auction.WinningBid ?? 0);

            // Publish AuctionEndedEvent to RabbitMQ
            await eventBus.PublishAsync(new AuctionEndedEvent
            {
                AuctionId = auction.Id,
                Title = auction.Title,
                WinnerName = auction.WinnerName,
                WinningBid = auction.WinningBid,
                FinalPrice = auction.CurrentPrice,
                EndedAt = now,
                HasWinner = auction.WinnerName != null
            });
        }
    }
}

