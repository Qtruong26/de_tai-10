using Microsoft.EntityFrameworkCore;
using AuctionSystem.AuditWorker;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.EventHandlers;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Data Source=auction_database.db";

builder.Services.AddDbContext<AuctionDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Configure RabbitMQ Event Bus for Audit Worker (Queue: audit_worker_queue)
builder.Services.AddAuctionEventBus(builder.Configuration, defaultQueueName: "audit_worker_queue");

builder.Services.AddTransient<AuditBidAcceptedHandler>();
builder.Services.AddTransient<AuditAuctionEndedHandler>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
