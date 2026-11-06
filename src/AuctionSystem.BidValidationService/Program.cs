using Microsoft.AspNetCore.Server.Kestrel.Core;
using AuctionSystem.BidValidationService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // Listen on port 5001 for HTTP/2 gRPC (unencrypted plain text)
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<BidValidationGrpcService>();

Console.WriteLine("==================================================================");
Console.WriteLine(" [gRPC] BID VALIDATION SERVICE IS RUNNING ON PORT 5001 (HTTP/2)");
Console.WriteLine(" Proto: Protos/bid_validation.proto -> Service: BidValidation");
Console.WriteLine("==================================================================");

app.Run();
