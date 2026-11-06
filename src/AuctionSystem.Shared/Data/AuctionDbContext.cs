using Microsoft.EntityFrameworkCore;
using AuctionSystem.Shared.Models;

namespace AuctionSystem.Shared.Data;

public class AuctionDbContext : DbContext
{
    public AuctionDbContext(DbContextOptions<AuctionDbContext> options) : base(options)
    {
    }

    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var dateTimeConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
            }
        }

        modelBuilder.Entity<Auction>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Title).IsRequired().HasMaxLength(255);
            entity.Property(a => a.StartingPrice).HasPrecision(18, 2);
            entity.Property(a => a.CurrentPrice).HasPrecision(18, 2);
            entity.Property(a => a.MinStep).HasPrecision(18, 2);
            entity.Property(a => a.WinningBid).HasPrecision(18, 2);
            entity.HasMany(a => a.Bids)
                  .WithOne(b => b.Auction)
                  .HasForeignKey(b => b.AuctionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bid>(entity =>
        {
            entity.HasKey(b => b.Id);
            entity.Property(b => b.BidderName).IsRequired().HasMaxLength(150);
            entity.Property(b => b.Amount).HasPrecision(18, 2);
            entity.HasIndex(b => b.AuctionId);
            entity.HasIndex(b => b.Timestamp);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(al => al.Id);
            entity.Property(al => al.EventType).IsRequired().HasMaxLength(100);
            entity.Property(al => al.Amount).HasPrecision(18, 2);
            entity.HasIndex(al => al.AuctionId);
            entity.HasIndex(al => al.Timestamp);
        });
    }
}

