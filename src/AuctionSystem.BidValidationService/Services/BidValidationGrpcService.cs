using Grpc.Core;
using AuctionSystem.Shared.Protos;

namespace AuctionSystem.BidValidationService.Services;

public class BidValidationGrpcService : BidValidation.BidValidationBase
{
    private readonly ILogger<BidValidationGrpcService> _logger;

    public BidValidationGrpcService(ILogger<BidValidationGrpcService> logger)
    {
        _logger = logger;
    }

    public override Task<ValidateBidResponse> ValidateBid(ValidateBidRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "[gRPC ValidateBid] Received request for AuctionId: {AuctionId}, Bidder: {Bidder}, Amount: {Amount}",
            request.AuctionId, request.BidderName, request.BidAmount);

        // 1. Check bidder name
        if (string.IsNullOrWhiteSpace(request.BidderName))
        {
            return Task.FromResult(new ValidateBidResponse
            {
                IsValid = false,
                ErrorCode = "INVALID_BIDDER",
                ErrorMessage = "Tên người trả giá không được để trống.",
                SuggestedMinBid = request.CurrentPrice > 0 ? request.CurrentPrice + request.MinStep : request.StartingPrice
            });
        }

        // 2. Check auction status
        if (!string.Equals(request.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ValidateBidResponse
            {
                IsValid = false,
                ErrorCode = "AUCTION_NOT_ACTIVE",
                ErrorMessage = $"Phiên đấu giá đang ở trạng thái '{request.Status}', không chấp nhận lượt trả giá mới.",
                SuggestedMinBid = 0
            });
        }

        // 3. Check time validity
        var nowEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (request.StartTimeEpoch > 0 && nowEpoch < request.StartTimeEpoch)
        {
            return Task.FromResult(new ValidateBidResponse
            {
                IsValid = false,
                ErrorCode = "AUCTION_NOT_STARTED",
                ErrorMessage = "Phiên đấu giá chưa bắt đầu.",
                SuggestedMinBid = request.StartingPrice
            });
        }

        if (request.EndTimeEpoch > 0 && nowEpoch >= request.EndTimeEpoch)
        {
            return Task.FromResult(new ValidateBidResponse
            {
                IsValid = false,
                ErrorCode = "AUCTION_ALREADY_ENDED",
                ErrorMessage = "Phiên đấu giá đã kết thúc.",
                SuggestedMinBid = 0
            });
        }

        // 4. Check if already the highest bidder
        if (!string.IsNullOrEmpty(request.CurrentHighestBidder) &&
            string.Equals(request.CurrentHighestBidder.Trim(), request.BidderName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new ValidateBidResponse
            {
                IsValid = false,
                ErrorCode = "ALREADY_HIGHEST_BIDDER",
                ErrorMessage = "Bạn hiện đang là người trả giá cao nhất. Vui lòng đợi người khác ra giá trước khi trả giá tiếp.",
                SuggestedMinBid = request.CurrentPrice + request.MinStep
            });
        }

        // 5. Check price increment rules
        double minRequiredBid;
        if (request.CurrentPrice <= 0)
        {
            minRequiredBid = request.StartingPrice;
        }
        else
        {
            minRequiredBid = request.CurrentPrice + (request.MinStep > 0 ? request.MinStep : 1000);
        }

        if (request.BidAmount < minRequiredBid)
        {
            string detail = request.CurrentPrice <= 0
                ? $"Mức giá trả ({request.BidAmount:N0} VND) phải tối thiểu bằng giá khởi điểm ({request.StartingPrice:N0} VND)."
                : $"Mức giá trả ({request.BidAmount:N0} VND) phải lớn hơn hoặc bằng {minRequiredBid:N0} VND (Giá hiện tại: {request.CurrentPrice:N0} VND, bước giá tối thiểu: {request.MinStep:N0} VND).";

            return Task.FromResult(new ValidateBidResponse
            {
                IsValid = false,
                ErrorCode = "BID_TOO_LOW",
                ErrorMessage = detail,
                SuggestedMinBid = minRequiredBid
            });
        }

        // Passed all validation checks
        _logger.LogInformation(
            "[gRPC ValidateBid SUCCESS] AuctionId: {AuctionId}, Bidder: {Bidder}, Valid Amount: {Amount}",
            request.AuctionId, request.BidderName, request.BidAmount);

        return Task.FromResult(new ValidateBidResponse
        {
            IsValid = true,
            ErrorCode = "OK",
            ErrorMessage = "Mức giá hợp lệ và được chấp nhận.",
            SuggestedMinBid = request.BidAmount + (request.MinStep > 0 ? request.MinStep : 10000)
        });
    }
}

