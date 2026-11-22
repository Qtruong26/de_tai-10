using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using AuctionSystem.Shared.DTOs;
using AuctionSystem.Shared.Events;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

var serverUrl = args.Length > 0 ? args[0] : "http://localhost:5000";
var http = new HttpClient { BaseAddress = new Uri(serverUrl) };

Console.WriteLine("==================================================================");
Console.WriteLine("  AUCTION SYSTEM - CONSOLE CLIENT (REST API + SIGNALR)");
Console.WriteLine($"  Kết nối tới Server: {serverUrl}");
Console.WriteLine("==================================================================");

// 1. Cấu hình và khởi tạo SignalR HubConnection (Yêu cầu C1)
var hubConnection = new HubConnectionBuilder()
    .WithUrl($"{serverUrl}/hubs/auction")
    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
    .Build();

hubConnection.Reconnecting += error =>
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n[SignalR] Đang kết nối lại tới máy chủ... ({error?.Message})");
    Console.ResetColor();
    return Task.CompletedTask;
};

hubConnection.Reconnected += connectionId =>
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n[SignalR] Đã kết nối lại thành công! ConnectionId: {connectionId}");
    Console.ResetColor();
    return Task.CompletedTask;
};

hubConnection.Closed += error =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n[SignalR] Kết nối đã đóng. ({error?.Message})");
    Console.ResetColor();
    return Task.CompletedTask;
};

// Đăng ký nhận sự kiện realtime từ SignalR Hub (C1 & D2)
hubConnection.On<AuctionResponse>("AuctionCreated", auction =>
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n>>> [REALTIME - NEW AUCTION] Phiên #{auction.Id[..8]} '{auction.Title}' vừa được tạo!");
    Console.WriteLine($"    Giá khởi điểm: {auction.StartingPrice:N0} VND | Người bán: {auction.SellerName} | Hạn: {auction.RemainingSeconds:N0}s");
    Console.ResetColor();
});

hubConnection.On<AuctionResponse>("AuctionUpdated", auction =>
{
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine($"\n>>> [REALTIME - UPDATED] Phiên #{auction.Id[..8]} '{auction.Title}' đã được cập nhật!");
    Console.WriteLine($"    Trạng thái: {auction.Status} | Bước giá: {auction.MinStep:N0} VND | Giá hiện tại: {auction.CurrentPrice:N0} VND");
    Console.ResetColor();
});

hubConnection.On<BidAcceptedEvent>("ReceiveBid", bid =>
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"\n>>> [REALTIME - NEW BID] Phiên #{bid.AuctionId[..8]} có lượt trả giá mới!");
    Console.WriteLine($"    Người trả giá: {bid.BidderName} | Mức giá mới: {bid.Amount:N0} VND (lúc {bid.Timestamp:HH:mm:ss})");
    Console.ResetColor();
});

hubConnection.On<AuctionEndedEvent>("AuctionEnded", ended =>
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n>>> [REALTIME - AUCTION ENDED] Phiên #{ended.AuctionId[..8]} '{ended.Title}' ĐÃ KẾT THÚC!");
    if (ended.HasWinner)
    {
        Console.WriteLine($"    🏆 Người chiến thắng: {ended.WinnerName} với mức giá {ended.FinalPrice:N0} VND");
    }
    else
    {
        Console.WriteLine($"    ❌ Không có người trả giá hợp lệ.");
    }
    Console.ResetColor();
});

hubConnection.On<object>("JoinedGroup", data =>
{
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine($"\n>>> [SignalR Room] {data}");
    Console.ResetColor();
});

// Kết nối SignalR
try
{
    await hubConnection.StartAsync();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"[SignalR] Kết nối thành công tới Hub /hubs/auction! (ConnectionId: {hubConnection.ConnectionId})");
    Console.ResetColor();
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[SignalR Cảnh báo] Chưa thể kết nối tới Hub: {ex.Message}");
    Console.WriteLine("                Hãy đảm bảo AuctionSystem.AuctionApi đang chạy trên cổng 5000.");
    Console.ResetColor();
}

// 2. Vòng lặp menu tương tác gọi REST API từ máy khách (Yêu cầu C2)
var running = true;
while (running)
{
    Console.WriteLine();
    Console.WriteLine("--------------------- MENU CHỨC NĂNG ---------------------");
    Console.WriteLine(" 1. [GET]  Xem danh sách tất cả phiên đấu giá");
    Console.WriteLine(" 2. [GET]  Xem chi tiết 1 phiên đấu giá theo ID");
    Console.WriteLine(" 3. [POST] Tạo phiên đấu giá mới (Kiểm tra 201 Created / 400 Bad Request)");
    Console.WriteLine(" 4. [POST] Đặt giá cho phiên (Kiểm tra 200 OK / 400 Bad Request / 409 Conflict)");
    Console.WriteLine(" 5. [PUT]  Cập nhật phiên đấu giá (Kiểm tra 200 OK / 404 Not Found)");
    Console.WriteLine(" 6. [POST] Kết thúc phiên đấu giá ngay lập tức");
    Console.WriteLine(" 7. [HUB]  Tham gia theo dõi nhóm riêng 1 phiên (SignalR JoinAuction)");
    Console.WriteLine(" 8. [POST] Reset dữ liệu demo về mặc định");
    Console.WriteLine(" 0. Thoát");
    Console.WriteLine("----------------------------------------------------------");
    Console.Write("Chọn chức năng (0-8): ");

    var choice = Console.ReadLine()?.Trim();
    Console.WriteLine();

    switch (choice)
    {
        case "1":
            await HandleGetAuctions(http);
            break;
        case "2":
            await HandleGetAuctionById(http);
            break;
        case "3":
            await HandleCreateAuction(http);
            break;
        case "4":
            await HandlePlaceBid(http);
            break;
        case "5":
            await HandleUpdateAuction(http);
            break;
        case "6":
            await HandleEndAuction(http);
            break;
        case "7":
            await HandleJoinRoom(hubConnection);
            break;
        case "8":
            await HandleResetDemo(http);
            break;
        case "0":
            running = false;
            break;
        default:
            Console.WriteLine("Lựa chọn không hợp lệ. Vui lòng chọn lại.");
            break;
    }
}

try
{
    await hubConnection.StopAsync();
    await hubConnection.DisposeAsync();
}
catch { }

Console.WriteLine("Đã đóng kết nối. Tạm biệt!");

// ==================== CÁC HÀM XỬ LÝ REST API (C2) ====================

static async Task HandleGetAuctions(HttpClient http)
{
    try
    {
        Console.WriteLine("[HTTP GET] Đang gửi yêu cầu tới /api/auctions ...");
        var response = await http.GetAsync("/api/auctions");
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");

        if (response.IsSuccessStatusCode)
        {
            var auctions = await response.Content.ReadFromJsonAsync<List<AuctionResponse>>();
            if (auctions == null || auctions.Count == 0)
            {
                Console.WriteLine("Chưa có phiên đấu giá nào ([]).");
                return;
            }

            Console.WriteLine($"\n--- DANH SÁCH {auctions.Count} PHIÊN ĐẤU GIÁ ---");
            Console.WriteLine(string.Format("{0,-10} | {1,-28} | {2,15} | {3,-10} | {4,-12}", "ID (ngắn)", "Tiêu đề", "Giá hiện tại", "Trạng thái", "Người dẫn đầu"));
            Console.WriteLine(new string('-', 85));
            foreach (var a in auctions)
            {
                Console.WriteLine(string.Format("{0,-10} | {1,-28} | {2,15:N0} | {3,-10} | {4,-12}",
                    a.Id[..Math.Min(8, a.Id.Length)],
                    a.Title.Length > 28 ? a.Title[..25] + "..." : a.Title,
                    a.CurrentPrice,
                    a.Status,
                    a.HighestBidder ?? "Chưa có"));
            }
        }
        else
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Lỗi máy chủ]: {error}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleGetAuctionById(HttpClient http)
{
    Console.Write("Nhập ID phiên đấu giá: ");
    var id = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(id)) return;

    try
    {
        Console.WriteLine($"[HTTP GET] Gửi yêu cầu tới /api/auctions/{id} ...");
        var response = await http.GetAsync($"/api/auctions/{id}");
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            var a = JsonSerializer.Deserialize<AuctionResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (a != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n--- CHI TIẾT PHIÊN #{a.Id} ---");
                Console.WriteLine($"Tiêu đề:         {a.Title}");
                Console.WriteLine($"Mô tả:           {a.Description}");
                Console.WriteLine($"Người bán:       {a.SellerName}");
                Console.WriteLine($"Giá khởi điểm:   {a.StartingPrice:N0} VND");
                Console.WriteLine($"Giá hiện tại:    {a.CurrentPrice:N0} VND");
                Console.WriteLine($"Bước giá min:    {a.MinStep:N0} VND");
                Console.WriteLine($"Trạng thái:      {a.Status}");
                Console.WriteLine($"Người giữ giá:   {a.HighestBidder ?? "Chưa có"}");
                Console.WriteLine($"Thời gian còn:   {a.RemainingSeconds:N0} giây");
                Console.ResetColor();
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Thông báo lỗi từ máy chủ]: {body}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleCreateAuction(HttpClient http)
{
    Console.WriteLine("--- TẠO PHIÊN ĐẤU GIÁ MỚI (POST /api/auctions) ---");
    Console.Write("Tiêu đề sản phẩm (để trống để test 400 Bad Request): ");
    var title = Console.ReadLine();

    Console.Write("Mô tả sản phẩm: ");
    var desc = Console.ReadLine() ?? string.Empty;

    Console.Write("Giá khởi điểm (VNĐ, nhập <=0 để test 400 Bad Request): ");
    var priceStr = Console.ReadLine();
    decimal.TryParse(priceStr, out var price);

    Console.Write("Bước giá tối thiểu (mặc định 50.000): ");
    var stepStr = Console.ReadLine();
    if (!decimal.TryParse(stepStr, out var minStep) || minStep <= 0) minStep = 50000m;

    Console.Write("Thời gian đấu giá (phút, mặc định 10): ");
    var durStr = Console.ReadLine();
    if (!int.TryParse(durStr, out var duration) || duration <= 0) duration = 10;

    Console.Write("Tên người bán (mặc định 'ShopCongNghe'): ");
    var seller = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(seller)) seller = "ShopCongNghe";

    var request = new CreateAuctionRequest
    {
        Title = title ?? string.Empty,
        Description = desc,
        StartingPrice = price,
        MinStep = minStep,
        DurationMinutes = duration,
        SellerName = seller
    };

    try
    {
        Console.WriteLine("\n[HTTP POST] Đang gửi yêu cầu tới /api/auctions ...");
        var response = await http.PostAsJsonAsync("/api/auctions", request);
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == System.Net.HttpStatusCode.Created)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[201 Created] Tạo phiên đấu giá thành công!");
            Console.WriteLine($"URI trỏ tới tài nguyên: {response.Headers.Location}");
            Console.WriteLine($"Chi tiết: {body}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Thất bại - Không phát SignalR]: {body}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandlePlaceBid(HttpClient http)
{
    Console.WriteLine("--- ĐẶT GIÁ ĐẤU (POST /api/auctions/{id}/bids) ---");
    Console.Write("Nhập ID phiên đấu giá: ");
    var id = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(id)) return;

    Console.Write("Tên người đấu giá (vd: NguyenVanA): ");
    var bidder = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(bidder)) bidder = "KhachHang_1";

    Console.Write("Mức giá muốn trả (VNĐ): ");
    var amountStr = Console.ReadLine()?.Trim();
    decimal.TryParse(amountStr, out var amount);

    var request = new CreateBidRequest
    {
        BidderName = bidder,
        Amount = amount
    };

    try
    {
        Console.WriteLine($"\n[HTTP POST] Gửi trả giá {amount:N0} VND tới /api/auctions/{id}/bids ...");
        var response = await http.PostAsJsonAsync($"/api/auctions/{id}/bids", request);
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[200 OK] Trả giá thành công!");
            Console.WriteLine($"Chi tiết: {body}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Lỗi từ máy chủ/gRPC/Concurrency]: {body}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleUpdateAuction(HttpClient http)
{
    Console.WriteLine("--- CẬP NHẬT PHIÊN ĐẤU GIÁ (PUT /api/auctions/{id}) - NÂNG CAO D1 ---");
    Console.Write("Nhập ID phiên đấu giá: ");
    var id = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(id)) return;

    Console.Write("Tiêu đề mới (bấm Enter để giữ nguyên): ");
    var title = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(title)) title = null;

    Console.Write("Mô tả mới (bấm Enter để giữ nguyên): ");
    var desc = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(desc)) desc = null;

    Console.Write("Bước giá mới (bấm Enter để giữ nguyên): ");
    var stepStr = Console.ReadLine();
    decimal? minStep = decimal.TryParse(stepStr, out var s) ? s : null;

    var request = new UpdateAuctionRequest
    {
        Title = title,
        Description = desc,
        MinStep = minStep
    };

    try
    {
        Console.WriteLine($"\n[HTTP PUT] Gửi yêu cầu cập nhật tới /api/auctions/{id} ...");
        var response = await http.PutAsJsonAsync($"/api/auctions/{id}", request);
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[200 OK] Cập nhật thành công! Máy chủ đã phát sự kiện SignalR AuctionUpdated.");
            Console.WriteLine($"Chi tiết: {body}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Lỗi]: {body}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleEndAuction(HttpClient http)
{
    Console.Write("Nhập ID phiên muốn kết thúc ngay: ");
    var id = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(id)) return;

    try
    {
        Console.WriteLine($"[HTTP POST] Gửi yêu cầu kết thúc tới /api/auctions/{id}/end ...");
        var response = await http.PostAsync($"/api/auctions/{id}/end", null);
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync();
        if (response.IsSuccessStatusCode)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[200 OK] Đã kết thúc phiên đấu giá! Sự kiện AuctionEnded đã phát qua RabbitMQ & SignalR.");
            Console.WriteLine($"Chi tiết: {body}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Lỗi]: {body}");
            Console.ResetColor();
        }
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleJoinRoom(HubConnection hub)
{
    Console.Write("Nhập ID phiên muốn vào phòng theo dõi riêng (SignalR JoinAuction): ");
    var id = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(id)) return;

    try
    {
        await hub.InvokeAsync("JoinAuction", id);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[SignalR] Đã gửi yêu cầu vào phòng theo dõi phiên {id}.");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[SignalR Lỗi]: {ex.Message}");
        Console.ResetColor();
    }
}

static async Task HandleResetDemo(HttpClient http)
{
    try
    {
        Console.WriteLine("[HTTP POST] Đang khôi phục dữ liệu mẫu ban đầu qua /api/auctions/reset-demo ...");
        var response = await http.PostAsync("/api/auctions/reset-demo", null);
        Console.WriteLine($"[HTTP Status] {(int)response.StatusCode} {response.StatusCode}");
        var body = await response.Content.ReadAsStringAsync();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(body);
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Lỗi kết nối]: {ex.Message}");
        Console.ResetColor();
    }
}
