using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using AuctionSystem.Shared.EventHandlers;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.Events;

namespace AuctionSystem.Tests;

public class AuditWorkerTests : IDisposable
{
    private readonly AuctionDbContext _db;
    private readonly IServiceProvider _serviceProvider;

    public AuditWorkerTests()
    {
        var dbName = "TestAuditDb_" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddDbContext<AuctionDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: dbName));

        _serviceProvider = services.BuildServiceProvider();
        _db = _serviceProvider.GetRequiredService<AuctionDbContext>();
    }

    [Fact]
    public async Task AuditBidAcceptedHandler_RecordsAuditLogToDb()
    {
        // Arrange
        var handler = new AuditBidAcceptedHandler(_serviceProvider, NullLogger<AuditBidAcceptedHandler>.Instance);
        var bidEvent = new BidAcceptedEvent
        {
            AuctionId = "auc-audit-1",
            BidId = "bid-123",
            BidderName = "TranVanB",
            Amount = 15000000m,
            NewCurrentPrice = 15000000m,
            Timestamp = DateTime.UtcNow
        };

        // Act
        await handler.HandleAsync(bidEvent);

        // Assert
        var logs = await _db.AuditLogs.Where(l => l.AuctionId == "auc-audit-1").ToListAsync();
        Assert.Single(logs);
        Assert.Equal("BidAccepted", logs[0].EventType);
        Assert.Equal("TranVanB", logs[0].BidderName);
        Assert.Equal(15000000m, logs[0].Amount);
        Assert.Contains("TranVanB", logs[0].Details);
    }

    [Fact]
    public async Task AuditAuctionEndedHandler_RecordsAuctionEndedToDb()
    {
        // Arrange
        var handler = new AuditAuctionEndedHandler(_serviceProvider, NullLogger<AuditAuctionEndedHandler>.Instance);
        var endEvent = new AuctionEndedEvent
        {
            AuctionId = "auc-audit-2",
            Title = "iPad Pro M4",
            WinnerName = "LeVanC",
            WinningBid = 24000000m,
            FinalPrice = 24000000m,
            EndedAt = DateTime.UtcNow,
            HasWinner = true
        };

        // Act
        await handler.HandleAsync(endEvent);

        // Assert
        var logs = await _db.AuditLogs.Where(l => l.AuctionId == "auc-audit-2").ToListAsync();
        Assert.Single(logs);
        Assert.Equal("AuctionEnded", logs[0].EventType);
        Assert.Equal("LeVanC", logs[0].BidderName);
        Assert.Equal(24000000m, logs[0].Amount);
        Assert.Contains("LeVanC", logs[0].Details);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
