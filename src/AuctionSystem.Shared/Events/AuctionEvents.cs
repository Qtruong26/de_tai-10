namespace AuctionSystem.Shared.Events;

public class BidAcceptedEvent
{
    public string AuctionId { get; set; } = string.Empty;
    public string BidId { get; set; } = string.Empty;
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal NewCurrentPrice { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class AuctionEndedEvent
{
    public string AuctionId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? WinnerName { get; set; }
    public decimal? WinningBid { get; set; }
    public decimal FinalPrice { get; set; }
    public DateTime EndedAt { get; set; } = DateTime.UtcNow;
    public bool HasWinner { get; set; }
}

