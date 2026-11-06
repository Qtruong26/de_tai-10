using AuctionSystem.Shared.Models;

namespace AuctionSystem.Shared.DTOs;

public class CreateAuctionRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public decimal MinStep { get; set; } = 10000m;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int? DurationMinutes { get; set; }
    public string SellerName { get; set; } = "Anonymous";
}

public class CreateBidRequest
{
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class AuctionResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal StartingPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal MinStep { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string? HighestBidder { get; set; }
    public string? WinnerName { get; set; }
    public decimal? WinningBid { get; set; }
    public int TotalBids { get; set; }
    public double RemainingSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BidResponse
{
    public string Id { get; set; } = string.Empty;
    public string AuctionId { get; set; } = string.Empty;
    public string BidderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsWinning { get; set; }
}

public class PlaceBidResult
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public decimal? SuggestedMinBid { get; set; }
    public BidResponse? Bid { get; set; }
    public AuctionResponse? Auction { get; set; }
}

