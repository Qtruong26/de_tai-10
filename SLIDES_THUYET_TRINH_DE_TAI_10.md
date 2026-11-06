# SLIDE THUYẾT TRÌNH BẢO VỆ ĐỒ ÁN
## ĐỀ TÀI 10 - HỆ THỐNG ĐẤU GIÁ TRỰC TUYẾN
### Kiến trúc Microservices: REST API, gRPC, RabbitMQ, SignalR

---

## SLIDE 1: GIỚI THIỆU ĐỀ TÀI & BÀI TOÁN

- **Tên đề tài**: Hệ thống đấu giá trực tuyến (Online Auction System).
- **Mục tiêu**:
  - Cho phép nhiều người dùng theo dõi trực tiếp một phiên đấu giá.
  - Đẩy mức giá mới theo thời gian thực (Real-time bidding).
  - Tách biệt nghiệp vụ kiểm tra mức giá qua dịch vụ độc lập với tốc độ cao.
  - Phân phối sự kiện bất đồng bộ để cập nhật giao diện và lưu vết kiểm toán mà không nghẽn hệ thống.

---

## SLIDE 2: KIẾN TRÚC TỔNG THỂ HỆ THỐNG (ARCHITECTURE)

```text
Bidder Clients -> Auction API -> gRPC -> Bid Validation Service
       ^              |
       | SignalR      | BidAccepted Event
       +-- Auction Hub <- RabbitMQ -> Audit Worker
                         |
                         v
                      Database
```

- **Mô hình**: Microservices hướng sự kiện (Event-Driven Architecture).
- **Nền tảng công nghệ**:
  - Backend: **.NET 9 / ASP.NET Core**
  - Giao thức nội bộ: **gRPC (HTTP/2, Protobuf)**
  - Message Broker: **RabbitMQ (Topic Exchange)**
  - Real-time Push: **ASP.NET Core SignalR (WebSocket)**
  - Cơ sở dữ liệu: **SQLite / Entity Framework Core 9**

---

## SLIDE 3: BID VALIDATION SERVICE (gRPC)

- **Tại sao sử dụng gRPC?**
  - Định dạng nhị phân Protobuf nhỏ gọn, serialize/deserialize cực nhanh.
  - Kết nối HTTP/2 multiplexing, độ trễ cực thấp (Sub-millisecond).
  - Tách rời hoàn toàn logic kiểm tra giá thành một microservice độc lập.
- **Quy tắc kiểm tra nghiệp vụ (6 lớp bảo vệ)**:
  1. `INVALID_BIDDER`: Xác thực danh tính người trả giá.
  2. `AUCTION_NOT_ACTIVE`: Kiểm tra trạng thái phiên mở/đóng.
  3. `AUCTION_NOT_STARTED`: Chặn trả giá khi phiên chưa đến giờ.
  4. `AUCTION_ALREADY_ENDED`: Chặn trả giá khi phiên đã hết thời gian.
  5. `ALREADY_HIGHEST_BIDDER`: Ngăn chặn người đang giữ giá tự đẩy giá của chính mình.
  6. `BID_TOO_LOW`: Bắt buộc mức giá mới $\ge$ `CurrentPrice + MinStep`.

---

## SLIDE 4: PHÂN PHỐI SỰ KIỆN QUA RABBITMQ

- **Exchange**: `auction.events` (Topic Exchange).
- **Cơ chế Publish / Subscribe**:
  - Khi có lượt trả giá hợp lệ: `BidAcceptedEvent` được phát lên exchange.
  - Khi phiên đấu giá hết giờ: `AuctionEndedEvent` được phát lên exchange.
- **Phân phối tới 2 hàng đợi độc lập**:
  - `auction_hub_queue` $\to$ **Auction Hub**: Đẩy dữ liệu ra ngoài Internet cho các Client qua SignalR.
  - `audit_worker_queue` $\to$ **Audit Worker**: Ghi nhật ký kiểm toán không thể sửa đổi vào Database.
- **Tính năng Resilient EventBus**:
  - Tự động kết nối RabbitMQ nếu có server AMQP.
  - Tự động kích hoạt kênh In-Memory EventBus nội bộ nếu chưa cài RabbitMQ, đảm bảo ứng dụng không bao giờ bị sập (Zero-crash resilience).

---

## SLIDE 5: CẬP NHẬT THỜI GIAN THỰC VỚI SIGNALR

- **Định tuyến Hub**: `/hubs/auction`.
- **Cơ chế Group Subscription**:
  - Client vào xem phòng đấu giá nào thì gọi `JoinAuction(auctionId)` để vào group đó.
  - Rời phòng thì gọi `LeaveAuction(auctionId)`.
- **Sự kiện Broadcast**:
  - `ReceiveBid`: Gửi mức giá mới, tên người trả giá, thời gian tới tất cả client trong cùng phòng đấu giá.
  - `AuctionEnded`: Thông báo phiên kết thúc, vinh danh người chiến thắng và mức giá thắng cuộc.
  - Client cập nhật DOM trực tiếp qua WebSocket mà **hoàn toàn không cần F5/tải lại trang**.

---

## SLIDE 6: AUDIT WORKER (LƯU VẾT KIỂM TOÁN)

- **Nguyên lý CQRS**: Tách biệt luồng xử lý ghi lệnh (Command) và luồng lưu vết (Audit).
- **Nhiệm vụ của Audit Worker**:
  - Chạy như một tiến trình worker độc lập (`BackgroundService`).
  - Lắng nghe sự kiện từ RabbitMQ một cách bất đồng bộ.
  - Ghi bản ghi vào bảng `AuditLogs`:
    - `BidAccepted`: Ghi nhận người trả, số tiền, giá sàn mới.
    - `AuctionEnded`: Ghi nhận người thắng, giá chốt, thời gian đóng phiên.
  - Đảm bảo tính minh bạch, hỗ trợ truy xuất và kiểm tra gian lận đấu giá.

---

## SLIDE 7: XỬ LÝ TƯƠNG TRANH & RACE CONDITION

- **Vấn đề**: Nhiều người dùng cùng bấm nút trả giá trong cùng một mili-giây.
- **Giải pháp bảo vệ**:
  - Kiểm tra tương tranh lạc quan (Optimistic Concurrency Control) trước khi ghi DB.
  - Nếu giá hiện tại trong DB đã bị người khác vượt qua trong lúc gRPC đang kiểm tra, hệ thống lập tức phát hiện và trả về mã lỗi `CONCURRENT_BID_DETECTED` (HTTP 409 Conflict).
  - Kết quả kiểm thử Stress Test 10 luồng đồng thời: Mức giá luôn tăng đơn điệu tuyệt đối, không có hiện tượng giá thấp đè lên giá cao.

---

## SLIDE 8: DANH SÁCH API RESTFUL HOÀN CHỈNH

```text
POST /auctions            - Tạo phiên đấu giá mới
GET  /auctions            - Danh sách các phiên (lọc theo trạng thái)
GET  /auctions/{id}       - Chi tiết phiên & thời gian đếm ngược
POST /auctions/{id}/bids  - Gửi lượt trả giá mới (gọi gRPC -> RabbitMQ)
GET  /auctions/{id}/bids  - Lấy lịch sử tất cả lượt trả giá
GET  /auctions/{id}/audits- Tra cứu nhật ký kiểm toán từ Audit Worker
POST /auctions/{id}/end   - Chốt phiên và công bố người thắng
POST /auctions/reset-demo - Khôi phục dữ liệu mẫu ban đầu để demo
```
- Tích hợp **Swagger UI tương tác** tại: `http://localhost:5000/swagger`.

---

## SLIDE 9: KẾT QUẢ KIỂM THỬ (TEST RESULTS)

- **Unit Tests (xUnit)**: **11/11 Passed (100%)**
  - Kiểm thử đầy đủ các case hợp lệ và vi phạm của gRPC BidValidator.
  - Kiểm thử REST API Controller và Mock gRPC Client.
  - Kiểm thử EventBus & Handler lưu DB của Audit Worker.
- **End-to-End Integration Test (`test_system.ps1`)**:
  - 8/8 bước kiểm thử tự động đều thành công trọn vẹn.
- **Concurrency Test (`test_concurrency.ps1`)**:
  - 10 bidder gửi giá đồng thời $\to$ Dữ liệu nhất quán 100%.

---

## SLIDE 10: TỔNG KẾT & KẾT LUẬN

- **Đáp ứng hoàn toàn 100% yêu cầu của Đề tài 10**:
  - Đúng chuẩn kiến trúc Microservices phân tán.
  - Tách biệt gRPC, RabbitMQ, SignalR, REST API, Database.
- **Mã nguồn hoàn chỉnh, sạch, dễ mở rộng**:
  - Có đầy đủ tài liệu hướng dẫn (`README.md`, `BAO_CAO_DE_TAI_10.md`).
  - Có script chạy 1-click (`run.ps1`) và container hóa (`docker-compose.yml`).
  - Giao diện người dùng thời gian thực đẹp mắt, trực quan và tiện dụng.
