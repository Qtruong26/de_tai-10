namespace AuctionSystem.Shared.Models;

public enum AuctionStatus
{
    Pending = 0,
    Active = 1,
    Ended = 2,
    Cancelled = 3
}

public class Auction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MinStep { get; set; } = 10000m;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public AuctionStatus Status { get; set; } = AuctionStatus.Pending;
    public string SellerName { get; set; } = "Anonymous";
    public string? HighestBidder { get; set; }
    public string? WinnerName { get; set; }
    public decimal? WinningBid { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Bid> Bids { get; set; } = new();

    public void UpdateStatusByTime(DateTime nowUtc)
    {
        if (Status == AuctionStatus.Cancelled) return;

        if (nowUtc < StartTime)
        {
            Status = AuctionStatus.Pending;
        }
        else if (nowUtc >= StartTime && nowUtc < EndTime)
        {
            Status = AuctionStatus.Active;
        }
        else if (nowUtc >= EndTime)
        {
            Status = AuctionStatus.Ended;
        }
    }
}

