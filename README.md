# Đề Tài 10 - Hệ Thống Đấu Giá Trực Tuyến (Online Auction System)

Hệ thống đấu giá trực tuyến xây dựng theo kiến trúc Microservices hiện đại, tuân thủ chính xác yêu cầu kỹ thuật của **Đề tài 10**:
- **Auction API (REST API + SignalR)**: Tiếp nhận yêu cầu đấu giá, tạo và tra cứu phiên đấu giá, phát sóng trực tiếp (real-time) biến động giá tới các Bidder Clients qua SignalR.
- **Bid Validation Service (gRPC Service)**: Dịch vụ độc lập kiểm tra tính hợp lệ của mức giá trả qua giao thức nhị phân gRPC tốc độ cao.
- **RabbitMQ (Message Broker)**: Phân phối bất đồng bộ các sự kiện `BidAcceptedEvent` và `AuctionEndedEvent` tới **Auction Hub** (SignalR) và **Audit Worker**.
- **Audit Worker**: Background worker độc lập lắng nghe sự kiện từ RabbitMQ để ghi nhật ký kiểm toán và lưu lịch sử phiên đấu giá vào cơ sở dữ liệu.
- **Bidder Web Client**: Giao diện người dùng trực quan, hỗ trợ phòng đấu giá thời gian thực với đồng hồ đếm ngược, đẩy giá real-time và thông báo người chiến thắng.

---

## 1. Sơ Đồ Kiến Trúc Hệ Thống

```text
┌────────────────┐          REST API          ┌───────────────────────────────────┐
│ Bidder Clients │ ─────────────────────────> │            Auction API            │
│  (Web / App)   │                            │  (ASP.NET Core REST + SignalR)    │
└────────────────┘                            └───────────────────────────────────┘
        ▲                                            │                      ▲
        │                                            │ gRPC                 │
        │ SignalR                                    ▼                      │
        │ Push                       ┌───────────────────────────────┐      │
        │                            │    Bid Validation Service     │      │
        │                            │         (gRPC Server)         │      │
        │                            └───────────────────────────────┘      │
        │                                                                   │
        │              BidAccepted / AuctionEnded Event                     │
        │         ┌─────────────────────────────────────────────────────────┘
        │         ▼
   ┌───────────────────────────────────────────────┐
   │             RabbitMQ Message Broker           │
   │           (Exchange: auction.events)          │
   └───────────────────────────────────────────────┘
        │                                    │
        ▼ Queue: auction_hub_queue           ▼ Queue: audit_worker_queue
┌───────────────────────────────┐   ┌───────────────────────────────┐
│          Auction Hub          │   │         Audit Worker          │
│    (SignalR Event Consumer)   │   │     (Audit Event Consumer)    │
└───────────────────────────────┘   └───────────────────────────────┘
        │                                    │
        │                                    ▼
        └───────────────────────────> ┌───────────────────────────────┐
                                      │           Database            │
                                      │       (SQLite / EF Core)      │
                                      └───────────────────────────────┘
```

---

## 2. Cấu Trúc Mã Nguồn (Solution Structure)

```text
d:\đề tài 10\
├── AuctionSystem.sln                             # Solution file chứa toàn bộ dự án
├── src\
│   ├── AuctionSystem.Shared\                     # Thư viện dùng chung
│   │   ├── Protos\bid_validation.proto           # Định nghĩa giao thức gRPC
│   │   ├── Models\                               # Entity: Auction, Bid, AuditLog
│   │   ├── Events\                               # Event: BidAcceptedEvent, AuctionEndedEvent
│   │   ├── DTOs\                                 # Request/Response DTOs
│   │   ├── Data\AuctionDbContext.cs              # EF Core DbContext
│   │   ├── EventBus\                             # IEventBus, RabbitMqEventBus, InMemoryEventBus
│   │   └── EventHandlers\                        # AuditBidAcceptedHandler, AuditAuctionEndedHandler
│   ├── AuctionSystem.BidValidationService\       # gRPC Microservice kiểm tra lượt trả giá (:5001)
│   │   └── Services\BidValidationGrpcService.cs  # Implementation gRPC
│   ├── AuctionSystem.AuctionApi\                 # REST API + SignalR Hub (:5000)
│   │   ├── Controllers\AuctionsController.cs     # REST API Endpoints
│   │   ├── Hubs\AuctionHub.cs                    # SignalR Hub cho phòng đấu giá
│   │   ├── EventHandlers\SignalREventHandlers.cs # Consumer RabbitMQ đẩy dữ liệu qua SignalR
│   │   ├── BackgroundServices\                   # Worker tự động kết thúc phiên hết hạn
│   │   └── wwwroot\                              # Single Page Web Dashboard (HTML/JS/CSS)
│   └── AuctionSystem.AuditWorker\                # Worker độc lập ghi nhật ký kiểm toán
│       └── Worker.cs                             # Lắng nghe RabbitMQ queue audit_worker_queue
├── tests\
│   └── AuctionSystem.Tests\                      # Bộ kiểm thử tự động xUnit (11/11 Passed)
│       ├── BidValidationServiceTests.cs          # Test nghiệp vụ gRPC Validator
│       ├── AuctionApiTests.cs                    # Test REST API Controller & Mock gRPC
│       └── AuditWorkerTests.cs                   # Test lưu vết Audit Worker
├── docker-compose.yml                            # Khởi chạy toàn bộ hệ thống với Docker & RabbitMQ
├── run.ps1                                       # Script khởi chạy 1-click trên Windows
└── test_system.ps1                               # Script kiểm tra tích hợp tự động End-to-End
```

---

## 3. Danh Sách API RESTful (Port 5000)

| Phương thức | Đường dẫn API | Mô tả |
|---|---|---|
| `POST` | `/auctions` | Tạo một phiên đấu giá mới với giá sàn, bước giá và thời lượng |
| `GET` | `/auctions` | Lấy danh sách tất cả các phiên (hỗ trợ lọc `?status=Active` hoặc `?status=Ended`) |
| `GET` | `/auctions/{id}` | Lấy chi tiết thông tin phiên, thời gian còn lại, người giữ giá hiện tại |
| `POST` | `/auctions/{id}/bids` | **Gửi lượt trả giá mới**: Tự động gọi gRPC kiểm tra -> Lưu DB -> Phát sự kiện RabbitMQ |
| `GET` | `/auctions/{id}/bids` | Lấy danh sách lịch sử trả giá của phiên đấu giá |
| `GET` | `/auctions/{id}/audits` | Tra cứu toàn bộ lịch sử kiểm toán của phiên do Audit Worker ghi lại |
| `POST` | `/auctions/{id}/end` | Kết thúc ngay phiên đấu giá, chốt người chiến thắng (phục vụ demo nhanh) |

---

## 4. Dịch Vụ gRPC (Port 5001)

### File định nghĩa Protobuf (`src/AuctionSystem.Shared/Protos/bid_validation.proto`):
```protobuf
syntax = "proto3";

package auction.validation;

option csharp_namespace = "AuctionSystem.Shared.Protos";

service BidValidation {
  rpc ValidateBid (ValidateBidRequest) returns (ValidateBidResponse);
}

message ValidateBidRequest {
  string auction_id = 1;
  string bidder_name = 2;
  double bid_amount = 3;
  double current_price = 4;
  double starting_price = 5;
  double min_step = 6;
  int64 start_time_epoch = 7;
  int64 end_time_epoch = 8;
  string status = 9;
  string current_highest_bidder = 10;
}

message ValidateBidResponse {
  bool is_valid = 1;
  string error_code = 2;
  string error_message = 3;
  double suggested_min_bid = 4;
}
```

### Các quy tắc kiểm tra của gRPC:
1. `INVALID_BIDDER`: Tên người trả giá không được để trống.
2. `AUCTION_NOT_ACTIVE`: Phiên đấu giá phải đang ở trạng thái `Active`.
3. `AUCTION_NOT_STARTED`: Thời gian hiện tại chưa đến giờ mở phiên.
4. `AUCTION_ALREADY_ENDED`: Phiên đấu giá đã kết thúc, không được trả thêm.
5. `ALREADY_HIGHEST_BIDDER`: Người dùng đang giữ giá cao nhất không được tự đẩy giá của chính mình.
6. `BID_TOO_LOW`: Mức giá trả phải lớn hơn hoặc bằng `CurrentPrice + MinStep` (hoặc `StartingPrice` nếu chưa có ai trả giá). Trả về gợi ý `suggested_min_bid`.

---

## 5. Phân Phối Sự Kiện Qua RabbitMQ

- **Exchange**: `auction.events` (Topic Exchange).
- **Queues**:
  1. `auction_hub_queue`: Auction Hub lắng nghe để đẩy dữ liệu tới web browser qua SignalR.
  2. `audit_worker_queue`: Audit Worker lắng nghe để ghi nhật ký kiểm toán vào Database.
- **Sự kiện chính**:
  - `BidAcceptedEvent`: Phát khi có một lượt trả giá hợp lệ được chấp nhận.
  - `AuctionEndedEvent`: Phát khi phiên đấu giá kết thúc và xác định người thắng cuộc.
- **Cơ chế Resilient Fallback**: Hệ thống tự động kết nối RabbitMQ nếu có server AMQP đang chạy (`localhost:5672`). Nếu môi trường máy tính chưa cài RabbitMQ, hệ thống tự động kích hoạt kênh In-Memory EventBus nội bộ mà **không hề bị lỗi hoặc gián đoạn luồng chạy**.

---

## 6. Hướng Dẫn Cài Đặt & Chạy Hệ Thống

### Cách 1: Chạy 1-Click bằng Script PowerShell (Khuyên dùng)

Mở PowerShell tại thư mục dự án và chạy:
```powershell
.\run.ps1
```
Script sẽ tự động:
1. Khởi chạy `BidValidationService` (gRPC tại port 5001).
2. Khởi chạy `AuctionApi` (REST API + SignalR tại port 5000).
3. Khởi chạy `AuditWorker`.
4. Mở trình duyệt web giao diện đấu giá tại `http://localhost:5000`.

---

### Cách 2: Chạy Từng Microservice Thủ Công

Mở 3 cửa sổ Terminal / PowerShell riêng biệt:

**Terminal 1 - Khởi chạy gRPC Bid Validation Service:**
```bash
dotnet run --project src/AuctionSystem.BidValidationService/AuctionSystem.BidValidationService.csproj --no-launch-profile
```

**Terminal 2 - Khởi chạy REST API & SignalR Hub:**
```bash
dotnet run --project src/AuctionSystem.AuctionApi/AuctionSystem.AuctionApi.csproj --no-launch-profile
```

**Terminal 3 - Khởi chạy Audit Worker:**
```bash
dotnet run --project src/AuctionSystem.AuditWorker/AuctionSystem.AuditWorker.csproj --no-launch-profile
```

Sau đó truy cập trình duyệt tại: **`http://localhost:5000`**

---

### Cách 3: Chạy Với Docker & Docker Compose (Kèm RabbitMQ Server)

Nếu máy có cài đặt Docker:
```bash
docker compose up --build -d
```
- Web Dashboard: `http://localhost:5000`
- RabbitMQ Management Dashboard: `http://localhost:15672` (User: `guest` / Pass: `guest`)

---

## 7. Kiểm Thử Hệ Thống (Testing)

### 1. Chạy Bộ Unit & Integration Tests (xUnit)
```bash
dotnet test
```
**Kết quả kiểm thử:**
- `Passed! - Failed: 0, Passed: 11, Skipped: 0, Total: 11`

### 2. Chạy Script Kiểm Tra Tự Động End-to-End
Khi các service đang chạy, mở một PowerShell khác và chạy:
```powershell
powershell -ExecutionPolicy Bypass -File .\test_system.ps1
```
Script sẽ tự động thực hiện kịch bản 8 bước:
1. `GET /auctions` -> Đọc danh sách phiên mẫu.
2. `POST /auctions` -> Tạo phiên mới.
3. `POST /auctions/{id}/bids` (Mức giá thấp) -> Chứng minh gRPC bắt lỗi `BID_TOO_LOW`.
4. `POST /auctions/{id}/bids` (Mức giá hợp lệ) -> Chứng minh gRPC duyệt, cập nhật DB, phát RabbitMQ.
5. `POST /auctions/{id}/bids` (Người thứ 2 vượt giá) -> Chứng minh biến động giá real-time.
6. `GET /auctions/{id}/bids` -> Kiểm tra bảng xếp hạng lượt trả giá.
7. `POST /auctions/{id}/end` -> Chốt phiên, công bố người chiến thắng.
8. `GET /auctions/{id}/audits` -> Kiểm tra lịch sử kiểm toán của Audit Worker.

---

## 8. Hướng Dẫn Trải Nghiệm Giao Diện Web Trực Tiếp

1. Mở trình duyệt tại `http://localhost:5000`.
2. Mở thêm 1 tab trình duyệt ẩn danh (Incognito) hoặc cửa sổ thứ 2.
3. Ở Tab 1: Đặt tên bạn là **"Alice"**.
4. Ở Tab 2: Đặt tên bạn là **"Bob"**.
5. Chọn cùng một phiên đấu giá (ví dụ: *iPhone 16 Pro Max*).
6. Ở Tab 1, bấm nút **+Bước giá** và nhấn **Gửi Mức Giá**.
7. Quan sát Tab 2: Mức giá và người giữ giá cao nhất sẽ **tự động nhảy tức thì qua SignalR** mà không cần F5 tải lại trang!
8. Nhấn nút **Chốt (Cờ đích)**: Phiên lập tức đóng lại, pháo hoa/banner vinh danh người thắng cuộc xuất hiện!
9. Chuyển sang tab **Nhật Ký Kiểm Toán** để xem Audit Worker đã ghi lại từng giao dịch.

