# Integration Verification Script for De Tai 10
param(
    [string]$ApiBase = "http://localhost:5000"
)

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " BAT DAU KIEM TRA HE THONG DAU GIA TRUC TUYEN (E2E TEST)" -ForegroundColor Yellow
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Test GET /auctions
Write-Host "`n[TEST 1] GET /auctions - Danh sach phien dau gia" -ForegroundColor Green
try {
    $auctions = Invoke-RestMethod -Uri "$ApiBase/auctions" -Method Get
    Write-Host "-> Thanh cong! Tim thay $($auctions.Count) phien dau gia ban dau." -ForegroundColor White
    $auctions | Select-Object -First 2 | Format-Table id, title, currentPrice, status
} catch {
    Write-Error "Khong the ket noi den Auction API: $_"
    exit 1
}

# 2. Test POST /auctions (Tao phien moi)
Write-Host "`n[TEST 2] POST /auctions - Tao phien dau gia moi" -ForegroundColor Green
$newAuctionBody = @{
    title = "Ban phim co Custom Keychron Q1 Pro (Wireless)"
    description = "Switch Gateron Jupiter Banana, keycap PBT Doubleshot, like new 99%"
    startingPrice = 3500000
    minStep = 100000
    durationMinutes = 15
    sellerName = "KeychronStore_VN"
} | ConvertTo-Json

$createdAuction = Invoke-RestMethod -Uri "$ApiBase/auctions" -Method Post -Body $newAuctionBody -ContentType "application/json"
Write-Host "-> Thanh cong! Da tao phien dau gia moi:" -ForegroundColor White
Write-Host "   ID: $($createdAuction.id)" -ForegroundColor White
Write-Host "   Tieu de: $($createdAuction.title)" -ForegroundColor White
Write-Host "   Gia khoi diem: $('{0:N0}' -f $createdAuction.startingPrice) VND" -ForegroundColor White
Write-Host "   Trang thai: $($createdAuction.status)" -ForegroundColor White

$testId = $createdAuction.id

# 3. Test POST /auctions/{id}/bids with INVALID BID (calling gRPC BidValidationService)
Write-Host "`n[TEST 3] POST /auctions/{id}/bids (MUC GIA THAP HON QUY DINH - KIEM TRA gRPC)" -ForegroundColor Green
$invalidBidBody = @{
    bidderName = "Alice"
    amount = 3550000 # Chỉ tăng 50k, trong khi minStep là 100k (3,600,000)
} | ConvertTo-Json

try {
    $badRes = Invoke-RestMethod -Uri "$ApiBase/auctions/$testId/bids" -Method Post -Body $invalidBidBody -ContentType "application/json"
    Write-Host "FAILED: Le ra phai bi tu choi!" -ForegroundColor Red
} catch {
    Write-Host "-> Thanh cong! gRPC BidValidationService da phat hien va tu choi muc gia khong hop le:" -ForegroundColor White
    try {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $errJson = $reader.ReadToEnd()
        $errObj = $errJson | ConvertFrom-Json
        Write-Host "   Error Code: $($errObj.errorCode)" -ForegroundColor Yellow
        Write-Host "   Thong bao: $($errObj.message)" -ForegroundColor Yellow
        Write-Host "   Gia toi thieu goi y: $('{0:N0}' -f $errObj.suggestedMinBid) VND" -ForegroundColor Yellow
    } catch {
        Write-Host "   Loi duoc bat thanh cong voi ma loi HTTP 400." -ForegroundColor Yellow
    }
}

# 4. Test POST /auctions/{id}/bids with VALID BID (calls gRPC -> saves DB -> emits RabbitMQ)
Write-Host "`n[TEST 4] POST /auctions/{id}/bids (MUC GIA HOP LE: 3,700,000 VND)" -ForegroundColor Green
$validBidBody = @{
    bidderName = "Alice"
    amount = 3700000
} | ConvertTo-Json

$validBidRes = Invoke-RestMethod -Uri "$ApiBase/auctions/$testId/bids" -Method Post -Body $validBidBody -ContentType "application/json"
Write-Host "-> Thanh cong! gRPC phe duyet, DB da cap nhat, su kien BidAccepted da phat qua RabbitMQ:" -ForegroundColor White
Write-Host "   Trang thai: $($validBidRes.success)" -ForegroundColor White
Write-Host "   Nguoi tra gia: $($validBidRes.bid.bidderName)" -ForegroundColor White
Write-Host "   Muc gia moi: $('{0:N0}' -f $validBidRes.bid.amount) VND" -ForegroundColor White

# 5. Test another Bidder (Bob) bids higher: 3,900,000 VND
Write-Host "`n[TEST 5] Nguoi dung Bob tra gia cao hon: 3,900,000 VND" -ForegroundColor Green
$bobBidBody = @{
    bidderName = "Bob"
    amount = 3900000
} | ConvertTo-Json

$bobBidRes = Invoke-RestMethod -Uri "$ApiBase/auctions/$testId/bids" -Method Post -Body $bobBidBody -ContentType "application/json"
Write-Host "-> Thanh cong! Bob da vuot gia Alice:" -ForegroundColor White
Write-Host "   Nguoi giu gia cao nhat hien tai: $($bobBidRes.auction.highestBidder)" -ForegroundColor White
Write-Host "   Gia hien tai: $('{0:N0}' -f $bobBidRes.auction.currentPrice) VND" -ForegroundColor White

# 6. Test GET /auctions/{id}/bids (Lich su tra gia)
Write-Host "`n[TEST 6] GET /auctions/{id}/bids - Xem lich su cac luot tra gia" -ForegroundColor Green
$bids = Invoke-RestMethod -Uri "$ApiBase/auctions/$testId/bids" -Method Get
Write-Host "-> Tim thay $($bids.Count) luot tra gia:" -ForegroundColor White
$bids | Format-Table bidderName, amount, timestamp, isWinning

# 7. Test POST /auctions/{id}/end (Ket thuc phien, xac dinh nguoi thang)
Write-Host "`n[TEST 7] POST /auctions/{id}/end - Ket thuc phien va xac dinh nguoi thang" -ForegroundColor Green
$endedAuction = Invoke-RestMethod -Uri "$ApiBase/auctions/$testId/end" -Method Post
Write-Host "-> Phien da ket thuc!" -ForegroundColor White
Write-Host "   Nguoi chien thang: $($endedAuction.winnerName)" -ForegroundColor Yellow
Write-Host "   Gia thang cuoc: $('{0:N0}' -f $endedAuction.winningBid) VND" -ForegroundColor Yellow
Write-Host "   Trang thai: $($endedAuction.status)" -ForegroundColor White

# Wait 1s for Audit Worker to consume event
Start-Sleep -Seconds 1

# 8. Test GET /auctions/{id}/audits (Tra cuu Audit Worker log)
Write-Host "`n[TEST 8] GET /auctions/{id}/audits - Nhat ky kiem toan tu Audit Worker" -ForegroundColor Green
$audits = Invoke-RestMethod -Uri "$ApiBase/auctions/$testId/audits" -Method Get
Write-Host "-> Tim thay $($audits.Count) ban ghi kiem toan:" -ForegroundColor White
$audits | Format-Table eventType, bidderName, amount, details, timestamp

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " TAT CA 8 MUC KIEM TRA END-TO-END DEU THANH CONG HOAN HAO!" -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Cyan
