using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AuctionSystem.AuctionApi.Data;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.DTOs;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.Events;
using AuctionSystem.Shared.Models;
using AuctionSystem.Shared.Protos;

namespace AuctionSystem.AuctionApi.Controllers;

[ApiController]
[Route("auctions")]
public class AuctionsController : ControllerBase
{
    private readonly AuctionDbContext _db;
    private readonly BidValidation.BidValidationClient _bidValidator;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AuctionsController> _logger;

    public AuctionsController(
        AuctionDbContext db,
        BidValidation.BidValidationClient bidValidator,
        IEventBus eventBus,
        ILogger<AuctionsController> logger)
    {
        _db = db;
        _bidValidator = bidValidator;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// POST /auctions - Tạo phiên đấu giá mới
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AuctionResponse>> CreateAuction([FromBody] CreateAuctionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { message = "Tiêu đề sản phẩm đấu giá không được để trống." });
        }

        if (request.StartingPrice <= 0)
        {
            return BadRequest(new { message = "Giá khởi điểm phải lớn hơn 0." });
        }

        var now = DateTime.UtcNow;
        var startTime = request.StartTime ?? now;
        var durationMinutes = request.DurationMinutes.GetValueOrDefault(5);
        var endTime = request.EndTime ?? startTime.AddMinutes(durationMinutes);

        if (endTime <= startTime)
        {
            return BadRequest(new { message = "Thời gian kết thúc phải lớn hơn thời gian bắt đầu." });
        }

        var auction = new Auction
        {
            Id = Guid.NewGuid().ToString(),
            Title = request.Title.Trim(),
            Description = request.Description ?? string.Empty,
            StartingPrice = request.StartingPrice,
            CurrentPrice = request.StartingPrice,
            MinStep = request.MinStep > 0 ? request.MinStep : 10000m,
            StartTime = startTime,
            EndTime = endTime,
            SellerName = string.IsNullOrWhiteSpace(request.SellerName) ? "Anonymous" : request.SellerName.Trim(),
            Status = now >= startTime && now < endTime ? AuctionStatus.Active : (now < startTime ? AuctionStatus.Pending : AuctionStatus.Ended),
            CreatedAt = now
        };

        _db.Auctions.Add(auction);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[REST API] Created Auction '{Title}' (ID: {Id})", auction.Title, auction.Id);

        return CreatedAtAction(nameof(GetAuctionById), new { id = auction.Id }, MapToResponse(auction));
    }

    /// <summary>
    /// GET /auctions - Danh sách các phiên đấu giá
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AuctionResponse>>> GetAuctions([FromQuery] string? status)
    {
        var now = DateTime.UtcNow;
        var query = _db.Auctions.Include(a => a.Bids).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<AuctionStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(a => a.Status == parsedStatus);
        }

        var auctions = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return Ok(auctions.Select(a => MapToResponse(a, now)));
    }

    /// <summary>
    /// GET /auctions/{id} - Chi tiết một phiên đấu giá
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionResponse>> GetAuctionById(string id)
    {
        var now = DateTime.UtcNow;
        var auction = await _db.Auctions
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (auction == null)
        {
            return NotFound(new { message = $"Không tìm thấy phiên đấu giá với ID '{id}'." });
        }

        return Ok(MapToResponse(auction, now));
    }

    /// <summary>
    /// POST /auctions/{id}/bids - Gửi mức giá mới (gọi gRPC BidValidationService -> RabbitMQ -> SignalR)
    /// </summary>
    [HttpPost("{id}/bids")]
    public async Task<ActionResult<PlaceBidResult>> PlaceBid(string id, [FromBody] CreateBidRequest request)
    {
        var auction = await _db.Auctions
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (auction == null)
        {
            return NotFound(new PlaceBidResult
            {
                Success = false,
                ErrorCode = "AUCTION_NOT_FOUND",
                Message = $"Không tìm thấy phiên đấu giá với ID '{id}'."
            });
        }

        var now = DateTime.UtcNow;
        var startTimeUtc = DateTime.SpecifyKind(auction.StartTime, DateTimeKind.Utc);
        var endTimeUtc = DateTime.SpecifyKind(auction.EndTime, DateTimeKind.Utc);

        // Auto check if expired
        if (auction.Status == AuctionStatus.Active && now >= endTimeUtc)
        {
            auction.Status = AuctionStatus.Ended;
            await _db.SaveChangesAsync();
        }

        // 1. Call Bid Validation Service via gRPC
        _logger.LogInformation("[REST API -> gRPC] Calling BidValidationService for Auction {AuctionId}, Amount: {Amount:N0} VND", id, request.Amount);

        var grpcRequest = new ValidateBidRequest
        {
            AuctionId = auction.Id,
            BidderName = request.BidderName ?? string.Empty,
            BidAmount = (double)request.Amount,
            CurrentPrice = (double)auction.CurrentPrice,
            StartingPrice = (double)auction.StartingPrice,
            MinStep = (double)auction.MinStep,
            StartTimeEpoch = new DateTimeOffset(startTimeUtc).ToUnixTimeSeconds(),
            EndTimeEpoch = new DateTimeOffset(endTimeUtc).ToUnixTimeSeconds(),
            Status = auction.Status.ToString(),
            CurrentHighestBidder = auction.HighestBidder ?? string.Empty
        };

        ValidateBidResponse grpcResponse;
        try
        {
            grpcResponse = await _bidValidator.ValidateBidAsync(grpcRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[REST API -> gRPC ERROR] Failed to communicate with BidValidationService.");
            return StatusCode(503, new PlaceBidResult
            {
                Success = false,
                ErrorCode = "VALIDATION_SERVICE_UNAVAILABLE",
                Message = "Dịch vụ kiểm tra giá (gRPC) tạm thời không khả dụng. Vui lòng thử lại sau."
            });
        }

        // 2. Handle gRPC Validation Failure
        if (!grpcResponse.IsValid)
        {
            _logger.LogWarning("[gRPC REJECTED] Auction {AuctionId}, Code: {Code}, Reason: {Reason}", id, grpcResponse.ErrorCode, grpcResponse.ErrorMessage);
            return BadRequest(new PlaceBidResult
            {
                Success = false,
                ErrorCode = grpcResponse.ErrorCode,
                Message = grpcResponse.ErrorMessage,
                SuggestedMinBid = (decimal)grpcResponse.SuggestedMinBid
            });
        }

        // Concurrency protection: Check if another bid was accepted while gRPC was validating
        var latestAuction = await _db.Auctions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (latestAuction != null && latestAuction.CurrentPrice >= request.Amount)
        {
            _logger.LogWarning("[Concurrency Conflict] Auction {AuctionId} price changed to {LatestPrice:N0} during validation of bid {Amount:N0}",
                id, latestAuction.CurrentPrice, request.Amount);
            return Conflict(new PlaceBidResult
            {
                Success = false,
                ErrorCode = "CONCURRENT_BID_DETECTED",
                Message = $"Đã có người dùng khác vừa trả giá {latestAuction.CurrentPrice:N0} VND cao hơn hoặc bằng mức giá của bạn. Vui lòng thử lại với mức giá mới!",
                SuggestedMinBid = latestAuction.CurrentPrice + latestAuction.MinStep
            });
        }

        // 3. Bid is VALID: Save bid to Database and update Auction
        var bidderName = (request.BidderName ?? "Anonymous").Trim();
        var bid = new Bid
        {
            Id = Guid.NewGuid().ToString(),
            AuctionId = auction.Id,
            BidderName = bidderName,
            Amount = request.Amount,
            Timestamp = DateTime.UtcNow,
            IsWinning = false
        };

        auction.CurrentPrice = request.Amount;
        auction.HighestBidder = bidderName;
        auction.Bids.Add(bid);

        _db.Bids.Add(bid);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[REST API -> DB] Saved valid bid {BidId} for Auction {AuctionId}", bid.Id, auction.Id);

        // 4. Publish BidAcceptedEvent via RabbitMQ
        var bidAcceptedEvent = new BidAcceptedEvent
        {
            AuctionId = auction.Id,
            BidId = bid.Id,
            BidderName = bid.BidderName,
            Amount = bid.Amount,
            NewCurrentPrice = auction.CurrentPrice,
            Timestamp = bid.Timestamp
        };

        await _eventBus.PublishAsync(bidAcceptedEvent, "auction.bid.accepted");

        return Ok(new PlaceBidResult
        {
            Success = true,
            ErrorCode = "OK",
            Message = "Trả giá thành công!",
            SuggestedMinBid = auction.CurrentPrice + auction.MinStep,
            Bid = new BidResponse
            {
                Id = bid.Id,
                AuctionId = bid.AuctionId,
                BidderName = bid.BidderName,
                Amount = bid.Amount,
                Timestamp = bid.Timestamp,
                IsWinning = false
            },
            Auction = MapToResponse(auction, now)
        });
    }

    /// <summary>
    /// GET /auctions/{id}/bids - Danh sách lịch sử trả giá của phiên
    /// </summary>
    [HttpGet("{id}/bids")]
    public async Task<ActionResult<IEnumerable<BidResponse>>> GetAuctionBids(string id)
    {
        var auctionExists = await _db.Auctions.AnyAsync(a => a.Id == id);
        if (!auctionExists)
        {
            return NotFound(new { message = $"Không tìm thấy phiên đấu giá với ID '{id}'." });
        }

        var bids = await _db.Bids
            .Where(b => b.AuctionId == id)
            .OrderByDescending(b => (double)b.Amount)
            .ThenByDescending(b => b.Timestamp)
            .ToListAsync();

        return Ok(bids.Select(b => new BidResponse
        {
            Id = b.Id,
            AuctionId = b.AuctionId,
            BidderName = b.BidderName,
            Amount = b.Amount,
            Timestamp = b.Timestamp,
            IsWinning = b.IsWinning
        }));
    }

    /// <summary>
    /// GET /auctions/{id}/audits - Tra cứu lịch sử kiểm toán của phiên đấu giá
    /// </summary>
    [HttpGet("{id}/audits")]
    public async Task<ActionResult<IEnumerable<AuditLog>>> GetAuctionAudits(string id)
    {
        var logs = await _db.AuditLogs
            .Where(a => a.AuctionId == id)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// POST /auctions/{id}/end - Kết thúc ngay phiên đấu giá (tiện cho demo và test)
    /// </summary>
    [HttpPost("{id}/end")]
    public async Task<ActionResult<AuctionResponse>> EndAuctionManually(string id)
    {
        var auction = await _db.Auctions
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (auction == null)
        {
            return NotFound(new { message = $"Không tìm thấy phiên đấu giá với ID '{id}'." });
        }

        if (auction.Status == AuctionStatus.Ended)
        {
            return BadRequest(new { message = "Phiên đấu giá đã kết thúc trước đó." });
        }

        var now = DateTime.UtcNow;
        auction.Status = AuctionStatus.Ended;
        auction.EndTime = now;

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

        await _db.SaveChangesAsync();

        // Phát sự kiện AuctionEnded qua RabbitMQ
        await _eventBus.PublishAsync(new AuctionEndedEvent
        {
            AuctionId = auction.Id,
            Title = auction.Title,
            WinnerName = auction.WinnerName,
            WinningBid = auction.WinningBid,
            FinalPrice = auction.CurrentPrice,
            EndedAt = now,
            HasWinner = auction.WinnerName != null
        });

        return Ok(MapToResponse(auction, now));
    }

    /// <summary>
    /// POST /auctions/reset-demo - Đặt lại cơ sở dữ liệu về trạng thái mẫu ban đầu để tiện demo
    /// </summary>
    [HttpPost("reset-demo")]
    public async Task<ActionResult> ResetDemoData()
    {
        _db.Bids.RemoveRange(_db.Bids);
        _db.AuditLogs.RemoveRange(_db.AuditLogs);
        _db.Auctions.RemoveRange(_db.Auctions);
        await _db.SaveChangesAsync();

        DbInitializer.Initialize(_db);

        _logger.LogInformation("[REST API] Reset database with fresh sample demo auctions.");
        return Ok(new { message = "Đã khôi phục thành công các phiên đấu giá mẫu ban đầu!" });
    }

    private static AuctionResponse MapToResponse(Auction auction, DateTime? now = null)
    {
        var currentUtc = now ?? DateTime.UtcNow;
        var endTimeUtc = DateTime.SpecifyKind(auction.EndTime, DateTimeKind.Utc);
        var remainingSeconds = (endTimeUtc - currentUtc).TotalSeconds;
        if (remainingSeconds < 0 || auction.Status == AuctionStatus.Ended)
        {
            remainingSeconds = 0;
        }

        return new AuctionResponse
        {
            Id = auction.Id,
            Title = auction.Title,
            Description = auction.Description,
            StartingPrice = auction.StartingPrice,
            CurrentPrice = auction.CurrentPrice,
            MinStep = auction.MinStep,
            StartTime = auction.StartTime,
            EndTime = auction.EndTime,
            Status = auction.Status.ToString(),
            SellerName = auction.SellerName,
            HighestBidder = auction.HighestBidder,
            WinnerName = auction.WinnerName,
            WinningBid = auction.WinningBid,
            TotalBids = auction.Bids?.Count ?? 0,
            RemainingSeconds = Math.Round(remainingSeconds, 1),
            CreatedAt = auction.CreatedAt
        };
    }
}
