namespace AuctionSystem.Shared.Models;

public class AuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string AuctionId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? BidderName { get; set; }
    public decimal? Amount { get; set; }
    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

