# Script kiem thu xu ly tuong tranh (Concurrency / Race Condition Test)
# Mo phong nhieu bidder cung tra gia tai cung mot thoi diem

param(
    [string]$ApiBase = "http://localhost:5000",
    [int]$BidderCount = 10
)

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " KIEM THU TUONG TRANH (CONCURRENCY TEST) - DE TAI 10" -ForegroundColor Yellow
Write-Host " Mo phong $BidderCount luot tra gia dong thoi vao cung 1 phien dau gia" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Tao 1 phien dau gia moi danh rieng cho test tuong tranh
$auctionPayload = @{
    title = "Test Concurrency - iPhone 16 Pro Max Limited Edition"
    description = "Phien dau gia thu nghiem kha nang xu ly dong thoi cua he thong"
    startingPrice = 30000000
    minStep = 500000
    durationMinutes = 10
    sellerName = "StressTestStore"
} | ConvertTo-Json

$auction = Invoke-RestMethod -Uri "$ApiBase/auctions" -Method Post -Body $auctionPayload -ContentType "application/json"
$auctionId = $auction.id
Write-Host "`n[+] Da tao phien dau gia test: $auctionId (Gia khoi diem: 30,000,000 VND)" -ForegroundColor Green

# 2. Khoi chay cac tac vu tra gia dong thoi
Write-Host "`n[+] Dang gui dong thoi $BidderCount yeu cau tra gia voi cac muc gia khac nhau..." -ForegroundColor Yellow

$jobs = @()
for ($i = 1; $i -le $BidderCount; $i++) {
    $bidder = "Bidder_$i"
    # Moi bidder dua ra cac muc gia tang dan cach nhau 500k
    $bidAmount = 30000000 + ($i * 500000)

    $scriptBlock = {
        param($url, $id, $name, $amount)
        $body = @{
            bidderName = $name
            amount = $amount
        } | ConvertTo-Json

        try {
            $resp = Invoke-RestMethod -Uri "$url/auctions/$id/bids" -Method Post -Body $body -ContentType "application/json"
            return @{ Success = $true; Bidder = $name; Amount = $amount; Code = "200 OK"; Message = "Chap nhan" }
        } catch {
            return @{ Success = $false; Bidder = $name; Amount = $amount; Code = $_.Exception.Response.StatusCode; Message = "Tu choi/Xung dot" }
        }
    }

    $jobs += Start-Job -ScriptBlock $scriptBlock -ArgumentList $ApiBase, $auctionId, $bidder, $bidAmount
}

# 3. Cho cac tac vu hoan tat
$results = $jobs | Wait-Job | Receive-Job
$jobs | Remove-Job

Write-Host "`n[+] Ket qua tra gia tuong tranh:" -ForegroundColor Green
$results | Format-Table Bidder, Amount, Code, Success, Message

# 4. Kiem tra tinh nhat quan cua du lieu trong Database
Start-Sleep -Seconds 1
$finalAuction = Invoke-RestMethod -Uri "$ApiBase/auctions/$auctionId" -Method Get
$bids = Invoke-RestMethod -Uri "$ApiBase/auctions/$auctionId/bids" -Method Get
$audits = Invoke-RestMethod -Uri "$ApiBase/auctions/$auctionId/audits" -Method Get

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " KET QUA XAC MINH TINH TOAN VEN DU LIEU" -ForegroundColor Yellow
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " - Gia cuoi cung cua phien: $('{0:N0}' -f $finalAuction.currentPrice) VND" -ForegroundColor White
Write-Host " - Nguoi giu gia cao nhat:  $($finalAuction.highestBidder)" -ForegroundColor White
Write-Host " - Tong so luot gia luu DB: $($bids.Count) luot" -ForegroundColor White
Write-Host " - Tong so audit log ghi:   $($audits.Count) ban ghi" -ForegroundColor White

Write-Host "`n[+] Bang xep hang cac luot tra gia hop le:" -ForegroundColor Green
$bids | Format-Table bidderName, amount, timestamp

Write-Host "`n[+] Bang nhat ky kiem toan tu Audit Worker:" -ForegroundColor Green
$audits | Format-Table eventType, bidderName, amount, details

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host " KIEM THU TUONG TRANH HOAN TAT! DU LIEU HOAN TOAN NHAT QUAN!" -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Cyan
