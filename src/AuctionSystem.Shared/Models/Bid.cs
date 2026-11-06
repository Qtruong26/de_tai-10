namespace AuctionSystem.Shared.Models;

public class Bid
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuctionId { get; set; } = string.Empty;
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsWinning { get; set; }

    public Auction? Auction { get; set; }
}

