# PowerShell script to start all services of the Auction System
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " HE THONG DAU GIA TRUC TUYEN - DE TAI 10" -ForegroundColor Yellow
Write-Host " Microservices Architecture: gRPC, REST API, SignalR, RabbitMQ" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Start BidValidationService (gRPC on port 5001)
Write-Host "[1/3] Khoi dong Bid Validation Service (gRPC :5001)..." -ForegroundColor Green
$grpcProcess = Start-Process dotnet -ArgumentList "run --project src/AuctionSystem.BidValidationService/AuctionSystem.BidValidationService.csproj" -PassThru

Start-Sleep -Seconds 3

# 2. Start AuctionApi (REST API + SignalR on port 5000)
Write-Host "[2/3] Khoi dong Auction API (REST + SignalR :5000)..." -ForegroundColor Green
$apiProcess = Start-Process dotnet -ArgumentList "run --project src/AuctionSystem.AuctionApi/AuctionSystem.AuctionApi.csproj" -PassThru

Start-Sleep -Seconds 3

# 3. Start AuditWorker (RabbitMQ consumer)
Write-Host "[3/3] Khoi dong Audit Worker (Event Consumer)..." -ForegroundColor Green
$workerProcess = Start-Process dotnet -ArgumentList "run --project src/AuctionSystem.AuditWorker/AuctionSystem.AuditWorker.csproj" -PassThru

Start-Sleep -Seconds 2

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " TAT CA CAC DICH VU DA SAN SANG!" -ForegroundColor Yellow
Write-Host " -> Dashboard Web: http://localhost:5000" -ForegroundColor White
Write-Host " -> Swagger/REST: http://localhost:5000/auctions" -ForegroundColor White
Write-Host " -> gRPC Endpoint: http://localhost:5001" -ForegroundColor White
Write-Host "==================================================================" -ForegroundColor Cyan

# Open browser
Start-Process "http://localhost:5000"

Write-Host "Nhan Enter de dung tat ca cac dich vu..." -ForegroundColor DarkYellow
Read-Host

Stop-Process -Id $grpcProcess.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
Stop-Process -Id $workerProcess.Id -Force -ErrorAction SilentlyContinue
Write-Host "Da dung cac dich vu." -ForegroundColor Red

