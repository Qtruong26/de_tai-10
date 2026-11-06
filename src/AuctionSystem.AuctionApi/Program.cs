using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using AuctionSystem.AuctionApi.BackgroundServices;
using AuctionSystem.AuctionApi.Data;
using AuctionSystem.AuctionApi.EventHandlers;
using AuctionSystem.AuctionApi.Hubs;
using AuctionSystem.Shared.Data;
using AuctionSystem.Shared.EventBus;
using AuctionSystem.Shared.EventHandlers;
using AuctionSystem.Shared.Events;
using AuctionSystem.Shared.Protos;

// Cho phép gọi HTTP/2 không mã hóa (h2c) cho giao tiếp nội bộ gRPC
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

// Cấu hình Kestrel lắng nghe port 5000 cho REST API & SignalR
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http1;
    });
});

// Cấu hình Database SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Data Source=auction_database.db";
builder.Services.AddDbContext<AuctionDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

// Đăng ký Controllers & SignalR
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();

// Đăng ký Swagger UI
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Auction System API - Đề tài 10",
        Version = "v1",
        Description = "Hệ thống đấu giá trực tuyến Microservices (REST API, gRPC, RabbitMQ, SignalR, Audit Worker)"
    });
});

// Đăng ký gRPC Client gọi tới BidValidationService (:5001)
var bidValidationUrl = builder.Configuration["GrpcServices:BidValidationUrl"] ?? "http://localhost:5001";
builder.Services.AddGrpcClient<BidValidation.BidValidationClient>(options =>
{
    options.Address = new Uri(bidValidationUrl);
});

// Cấu hình EventBus (kết nối RabbitMQ, tự động fallback sang In-Memory nếu chưa có RabbitMQ server)
builder.Services.AddAuctionEventBus(builder.Configuration, defaultQueueName: "auction_hub_queue");

// Đăng ký các Consumer xử lý sự kiện
builder.Services.AddTransient<BidAcceptedSignalRHandler>();
builder.Services.AddTransient<AuctionEndedSignalRHandler>();
builder.Services.AddTransient<AuditBidAcceptedHandler>();
builder.Services.AddTransient<AuditAuctionEndedHandler>();

// Background worker tự động quét và kết thúc các phiên đấu giá hết hạn
builder.Services.AddHostedService<AuctionExpirationWorker>();

// Cho phép CORS cho tất cả client
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Khởi tạo và nạp dữ liệu mẫu ban đầu vào Database
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuctionDbContext>();
    DbInitializer.Initialize(db);
}

// Đăng ký các Handler nhận sự kiện từ RabbitMQ
var eventBus = app.Services.GetRequiredService<IEventBus>();
eventBus.Subscribe<BidAcceptedEvent, BidAcceptedSignalRHandler>();
eventBus.Subscribe<AuctionEndedEvent, AuctionEndedSignalRHandler>();
eventBus.Subscribe<BidAcceptedEvent, AuditBidAcceptedHandler>();
eventBus.Subscribe<AuctionEndedEvent, AuditAuctionEndedHandler>();

app.UseCors("AllowAll");

// Kích hoạt Swagger & Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Auction System API v1");
    c.RoutePrefix = "swagger";
});

// Phục vụ giao diện Web Client Single Page App từ wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.MapControllers();
app.MapHub<AuctionHub>("/hubs/auction");

Console.WriteLine("==================================================================");
Console.WriteLine(" [REST API + SIGNALR] AUCTION API IS RUNNING ON PORT 5000");
Console.WriteLine(" -> Web Dashboard:     http://localhost:5000");
Console.WriteLine(" -> Swagger UI:        http://localhost:5000/swagger");
Console.WriteLine(" -> SignalR Hub:       http://localhost:5000/hubs/auction");
Console.WriteLine(" -> REST Endpoints:    http://localhost:5000/auctions");
Console.WriteLine($" -> gRPC Target:       {bidValidationUrl}");
Console.WriteLine("==================================================================");

app.Run();
