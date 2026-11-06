using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.Models;

namespace AuctionSystem.AuctionApi.Data;

public static class DbInitializer
{
    public static void Initialize(AuctionDbContext context)
    {
        context.Database.EnsureCreated();

        if (context.Auctions.Any())
        {
            return; // DB has already been seeded
        }

        var now = DateTime.UtcNow;

        var sampleAuctions = new List<Auction>
        {
            new()
            {
                Id = "auc-101",
                Title = "iPhone 16 Pro Max 256GB Desert Titanium (VN/A)",
                Description = "Mới 100% nguyên seal chưa kích hoạt, bảo hành chính hãng Apple Việt Nam 12 tháng.",
                StartingPrice = 28000000m,
                CurrentPrice = 28000000m,
                MinStep = 500000m,
                StartTime = now.AddMinutes(-10),
                EndTime = now.AddMinutes(20),
                Status = AuctionStatus.Active,
                SellerName = "Apple Authorized Store",
                CreatedAt = now.AddMinutes(-10)
            },
            new()
            {
                Id = "auc-102",
                Title = "MacBook Pro 16 M3 Max (36GB RAM / 1TB SSD)",
                Description = "Máy màu Space Black siêu đẹp, like new 99% fullbox, pin 100%.",
                StartingPrice = 65000000m,
                CurrentPrice = 67000000m,
                MinStep = 1000000m,
                StartTime = now.AddMinutes(-30),
                EndTime = now.AddMinutes(45),
                Status = AuctionStatus.Active,
                SellerName = "Macbook Saigon",
                HighestBidder = "HoangLong_IT",
                CreatedAt = now.AddMinutes(-30),
                Bids = new List<Bid>
                {
                    new()
                    {
                        Id = Guid.NewGuid().ToString(),
                        AuctionId = "auc-102",
                        BidderName = "HoangLong_IT",
                        Amount = 67000000m,
                        Timestamp = now.AddMinutes(-5),
                        IsWinning = false
                    }
                }
            },
            new()
            {
                Id = "auc-103",
                Title = "Đồng hồ cơ Thụy Sĩ Tissot Le Locle Powermatic 80",
                Description = "Mặt số la mã cổ điển, trữ cót lên đến 80 giờ, kính sapphire nguyên khối chống trầy.",
                StartingPrice = 8500000m,
                CurrentPrice = 8500000m,
                MinStep = 200000m,
                StartTime = now.AddMinutes(-5),
                EndTime = now.AddMinutes(15),
                Status = AuctionStatus.Active,
                SellerName = "Swiss Watch Hub",
                CreatedAt = now.AddMinutes(-5)
            },
            new()
            {
                Id = "auc-104",
                Title = "Sony PlayStation 5 Pro (2TB Edition)",
                Description = "Phiên bản PS5 Pro mới nhất, kèm 2 tay cầm DualSense và đĩa game Astro Bot.",
                StartingPrice = 18000000m,
                CurrentPrice = 19500000m,
                MinStep = 500000m,
                StartTime = now.AddMinutes(-60),
                EndTime = now.AddMinutes(-5), // Đã kết thúc
                Status = AuctionStatus.Ended,
                SellerName = "GamerZone VN",
                HighestBidder = "MinhTri_Gamer",
                WinnerName = "MinhTri_Gamer",
                WinningBid = 19500000m,
                CreatedAt = now.AddMinutes(-60),
                Bids = new List<Bid>
                {
                    new()
                    {
                        Id = Guid.NewGuid().ToString(),
                        AuctionId = "auc-104",
                        BidderName = "MinhTri_Gamer",
                        Amount = 19500000m,
                        Timestamp = now.AddMinutes(-6),
                        IsWinning = true
                    }
                }
            }
        };

        context.Auctions.AddRange(sampleAuctions);
        context.SaveChanges();
    }
}

