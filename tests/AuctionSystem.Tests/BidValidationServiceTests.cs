using Microsoft.Extensions.Logging.Abstractions;
using AuctionSystem.BidValidationService.Services;
using AuctionSystem.Shared.Protos;

namespace AuctionSystem.Tests;

public class BidValidationServiceTests
{
    private readonly BidValidationGrpcService _service;

    public BidValidationServiceTests()
    {
        _service = new BidValidationGrpcService(NullLogger<BidValidationGrpcService>.Instance);
    }

    [Fact]
    public async Task ValidateBid_ValidBid_ReturnsTrueAndOkCode()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new ValidateBidRequest
        {
            AuctionId = "auc-1",
            BidderName = "Alice",
            BidAmount = 15000000,
            CurrentPrice = 14000000,
            StartingPrice = 10000000,
            MinStep = 500000,
            StartTimeEpoch = now - 300,
            EndTimeEpoch = now + 1200,
            Status = "Active",
            CurrentHighestBidder = "Bob"
        };

        // Act
        var result = await _service.ValidateBid(request, null!);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("OK", result.ErrorCode);
        Assert.Equal(15500000, result.SuggestedMinBid);
    }

    [Fact]
    public async Task ValidateBid_EmptyBidder_ReturnsInvalidBidder()
    {
        // Arrange
        var request = new ValidateBidRequest
        {
            AuctionId = "auc-1",
            BidderName = "   ",
            BidAmount = 15000000,
            CurrentPrice = 10000000,
            StartingPrice = 10000000,
            MinStep = 500000,
            Status = "Active"
        };

        // Act
        var result = await _service.ValidateBid(request, null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("INVALID_BIDDER", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateBid_AuctionNotActive_ReturnsAuctionNotActive()
    {
        // Arrange
        var request = new ValidateBidRequest
        {
            AuctionId = "auc-1",
            BidderName = "Alice",
            BidAmount = 20000000,
            Status = "Pending"
        };

        // Act
        var result = await _service.ValidateBid(request, null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("AUCTION_NOT_ACTIVE", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateBid_AuctionExpired_ReturnsAuctionAlreadyEnded()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new ValidateBidRequest
        {
            AuctionId = "auc-1",
            BidderName = "Alice",
            BidAmount = 20000000,
            Status = "Active",
            StartTimeEpoch = now - 600,
            EndTimeEpoch = now - 60 // Already ended
        };

        // Act
        var result = await _service.ValidateBid(request, null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("AUCTION_ALREADY_ENDED", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateBid_BidBelowMinIncrement_ReturnsBidTooLow()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new ValidateBidRequest
        {
            AuctionId = "auc-1",
            BidderName = "Alice",
            BidAmount = 10200000, // Only +200k, while min step is 500k
            CurrentPrice = 10000000,
            StartingPrice = 10000000,
            MinStep = 500000,
            StartTimeEpoch = now - 300,
            EndTimeEpoch = now + 1200,
            Status = "Active",
            CurrentHighestBidder = "Bob"
        };

        // Act
        var result = await _service.ValidateBid(request, null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("BID_TOO_LOW", result.ErrorCode);
        Assert.Equal(10500000, result.SuggestedMinBid);
    }

    [Fact]
    public async Task ValidateBid_BidderAlreadyHighest_ReturnsAlreadyHighestBidder()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var request = new ValidateBidRequest
        {
            AuctionId = "auc-1",
            BidderName = "Alice",
            BidAmount = 15000000,
            CurrentPrice = 10000000,
            StartingPrice = 10000000,
            MinStep = 500000,
            StartTimeEpoch = now - 300,
            EndTimeEpoch = now + 1200,
            Status = "Active",
            CurrentHighestBidder = "Alice" // Same bidder
        };

        // Act
        var result = await _service.ValidateBid(request, null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal("ALREADY_HIGHEST_BIDDER", result.ErrorCode);
    }
}

