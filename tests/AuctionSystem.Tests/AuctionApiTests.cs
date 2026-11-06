using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AuctionSystem.AuctionApi.Controllers;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.DTOs;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.Events;
using AuctionSystem.Shared.Models;
using AuctionSystem.Shared.Protos;
using Grpc.Core;

namespace AuctionSystem.Tests;

public class AuctionApiTests : IDisposable
{
    private readonly AuctionDbContext _db;
    private readonly Mock<IEventBus> _mockEventBus;
    private readonly Mock<BidValidation.BidValidationClient> _mockGrpcClient;
    private readonly AuctionsController _controller;

    public AuctionApiTests()
    {
        var options = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AuctionDbContext(options);
        _mockEventBus = new Mock<IEventBus>();
        _mockGrpcClient = new Mock<BidValidation.BidValidationClient>();

        _controller = new AuctionsController(
            _db,
            _mockGrpcClient.Object,
            _mockEventBus.Object,
            NullLogger<AuctionsController>.Instance);
    }

    [Fact]
    public async Task CreateAuction_ValidRequest_SavesToDbAndReturns201()
    {
        // Arrange
        var request = new CreateAuctionRequest
        {
            Title = "MacBook Pro M3 Max",
            Description = "Fullbox like new",
            StartingPrice = 60000000m,
            MinStep = 1000000m,
            DurationMinutes = 10,
            SellerName = "TechShop"
        };

        // Act
        var result = await _controller.CreateAuction(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var auctionResponse = Assert.IsType<AuctionResponse>(createdResult.Value);
        Assert.Equal("MacBook Pro M3 Max", auctionResponse.Title);
        Assert.Equal(60000000m, auctionResponse.StartingPrice);
        Assert.Equal("Active", auctionResponse.Status);

        var savedInDb = await _db.Auctions.FindAsync(auctionResponse.Id);
        Assert.NotNull(savedInDb);
    }

    [Fact]
    public async Task PlaceBid_ValidBid_UpdatesAuctionAndPublishesRabbitMqEvent()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var auction = new Auction
        {
            Id = "auc-test-1",
            Title = "Sony WH-1000XM5",
            StartingPrice = 5000000m,
            CurrentPrice = 5000000m,
            MinStep = 200000m,
            StartTime = now.AddMinutes(-5),
            EndTime = now.AddMinutes(15),
            Status = AuctionStatus.Active,
            HighestBidder = null
        };
        _db.Auctions.Add(auction);
        await _db.SaveChangesAsync();

        // Setup gRPC mock to return Valid
        var grpcResponse = new ValidateBidResponse
        {
            IsValid = true,
            ErrorCode = "OK",
            ErrorMessage = "Mức giá hợp lệ.",
            SuggestedMinBid = 5400000
        };

        var mockCall = TestCalls.AsyncUnaryCall(
            Task.FromResult(grpcResponse),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        _mockGrpcClient
            .Setup(m => m.ValidateBidAsync(It.IsAny<ValidateBidRequest>(), null, null, default))
            .Returns(mockCall);

        var bidRequest = new CreateBidRequest
        {
            BidderName = "Bob",
            Amount = 5200000m
        };

        // Act
        var response = await _controller.PlaceBid("auc-test-1", bidRequest);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(response.Result);
        var placeResult = Assert.IsType<PlaceBidResult>(okResult.Value);
        Assert.True(placeResult.Success);
        Assert.Equal("Bob", placeResult.Bid!.BidderName);
        Assert.Equal(5200000m, placeResult.Bid.Amount);

        // Verify DB updated
        var updatedAuction = await _db.Auctions.Include(a => a.Bids).FirstAsync(a => a.Id == "auc-test-1");
        Assert.Equal(5200000m, updatedAuction.CurrentPrice);
        Assert.Equal("Bob", updatedAuction.HighestBidder);
        Assert.Single(updatedAuction.Bids);

        // Verify RabbitMQ EventBus published BidAcceptedEvent
        _mockEventBus.Verify(
            b => b.PublishAsync(It.Is<BidAcceptedEvent>(e => e.AuctionId == "auc-test-1" && e.BidderName == "Bob" && e.Amount == 5200000m), "auction.bid.accepted"),
            Times.Once);
    }

    [Fact]
    public async Task PlaceBid_WhenGrpcRejects_ReturnsBadRequestAndDoesNotSaveBid()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var auction = new Auction
        {
            Id = "auc-test-2",
            Title = "Rolex Submariner",
            StartingPrice = 200000000m,
            CurrentPrice = 200000000m,
            MinStep = 5000000m,
            StartTime = now.AddMinutes(-5),
            EndTime = now.AddMinutes(15),
            Status = AuctionStatus.Active,
            HighestBidder = null
        };
        _db.Auctions.Add(auction);
        await _db.SaveChangesAsync();

        // Setup gRPC mock to return Invalid (e.g. BID_TOO_LOW)
        var grpcResponse = new ValidateBidResponse
        {
            IsValid = false,
            ErrorCode = "BID_TOO_LOW",
            ErrorMessage = "Mức giá quá thấp.",
            SuggestedMinBid = 205000000
        };

        var mockCall = TestCalls.AsyncUnaryCall(
            Task.FromResult(grpcResponse),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        _mockGrpcClient
            .Setup(m => m.ValidateBidAsync(It.IsAny<ValidateBidRequest>(), null, null, default))
            .Returns(mockCall);

        var bidRequest = new CreateBidRequest
        {
            BidderName = "Bob",
            Amount = 201000000m
        };

        // Act
        var response = await _controller.PlaceBid("auc-test-2", bidRequest);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(response.Result);
        var placeResult = Assert.IsType<PlaceBidResult>(badRequestResult.Value);
        Assert.False(placeResult.Success);
        Assert.Equal("BID_TOO_LOW", placeResult.ErrorCode);

        // Verify DB was NOT updated
        var auctionInDb = await _db.Auctions.Include(a => a.Bids).FirstAsync(a => a.Id == "auc-test-2");
        Assert.Equal(200000000m, auctionInDb.CurrentPrice);
        Assert.Empty(auctionInDb.Bids);

        // Verify EventBus was NOT called
        _mockEventBus.Verify(
            b => b.PublishAsync(It.IsAny<BidAcceptedEvent>(), It.IsAny<string>()),
            Times.Never);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}

// Helper to mock gRPC AsyncUnaryCall
public static class TestCalls
{
    public static AsyncUnaryCall<TResponse> AsyncUnaryCall<TResponse>(
        Task<TResponse> response,
        Task<Metadata> responseHeaders,
        Func<Status> getStatus,
        Func<Metadata> getTrailers,
        Action dispose)
    {
        return new AsyncUnaryCall<TResponse>(response, responseHeaders, getStatus, getTrailers, dispose);
    }
}

