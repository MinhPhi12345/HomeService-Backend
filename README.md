# HomeService – Backend API

Backend dùng chung cho toàn bộ hệ thống **HomeService** (website khách hàng, website admin, app di động) — đề tài Công nghệ phần mềm Nâng cao, **Nhóm 15**. Code bám theo **SRS v1.3** và **Backlog v1.2** (8 Epic, 37 User Story, 7 Sprint).

- **Framework**: ASP.NET Core 8 Web API · **ORM**: EF Core 8 (Code First) · **DB**: SQL Server 2022
- **Auth**: JWT Bearer + BCrypt · **Docs**: Swagger tại `/swagger` (môi trường Development)
- **Tác vụ nền**: `BookingWorker` chạy mỗi 5 giây — ghép nối theo đợt, hủy đơn quá hạn thanh toán, tự xác nhận sau 24 giờ, hết hạn đơn chưa có Tasker, phát hiện Tasker không đến, nhắc lịch hẹn.

## 1. Chạy Backend

### Cách A – Docker (khuyến nghị, giống môi trường deploy VPS)

```bash
cp .env.example .env        # chỉnh SA_PASSWORD, JWT_KEY nếu cần
docker compose up --build
```

- API: `http://localhost:8080/api` · Swagger: `http://localhost:8080/swagger`
- **Không cần chạy `dotnet ef database update`**: lúc khởi động Backend tự áp dụng migration (thử lại tối đa 10 lần nếu SQL Server chưa sẵn sàng) và tạo dữ liệu ban đầu.

### Cách B – Chạy trực tiếp (Visual Studio / `dotnet run`) với SQL Server trên máy

1. Sửa chuỗi kết nối `ConnectionStrings:DefaultConnection` trong `appsettings.Development.json` (hoặc `dotnet user-secrets`):
   - SQL Server dùng tài khoản `sa`: `Server=localhost,1433;Database=HomeServiceDb;User Id=sa;Password=...;TrustServerCertificate=True;`
   - SQL Server Express đăng nhập Windows: `Server=localhost\SQLEXPRESS;Database=HomeServiceDb;Trusted_Connection=True;TrustServerCertificate=True;`
2. `dotnet run` → mở Swagger theo địa chỉ in ra (vd: `http://localhost:5xxx/swagger`). Nhớ đổi `baseUrl` trong Postman cho khớp cổng.

### Dữ liệu tạo sẵn khi khởi động

| Loại | Giá trị | Cấu hình |
|---|---|---|
| Tài khoản Admin | `admin@homeservice.vn` / `Admin@123` | `Seed:AdminEmail`, `Seed:AdminPassword` |
| Dịch vụ mẫu (chỉ khi bảng trống) | Dọn dẹp nhà, Giặt ủi, Sửa điện nước, Chăm sóc cây, Nấu ăn gia đình | `Seed:SampleData` |

> Đổi mật khẩu Admin và `Jwt:Key` trước khi deploy lên VPS (NFR-03).

## 2. Kiểm thử bằng Postman

1. Postman → **Import** → chọn `docs/postman/HomeService.postman_collection.json`.
2. Tab **Variables** của collection: kiểm tra `baseUrl` (mặc định `http://localhost:8080/api`).
3. Chạy **lần lượt** từ folder *Sprint 1* đến *Sprint 7* (Collection Runner hoặc bấm Send từng request). Token, `bookingId`, `disputeId`… được lưu tự động; mỗi request có sẵn test kiểm tra mã HTTP và thông báo lỗi theo SRS.
4. Request *US04 Tải ảnh lên*: ở Body → form-data chọn 1 ảnh JPG/PNG (có sẵn `docs/postman/sample.png`).

Một số kịch bản phụ thuộc thời gian thực, muốn xem nhanh thì chỉnh DB bằng SSMS (thay `<id>`):

| Kịch bản | SQL (SQL Server) | Kết quả sau ~5 giây |
|---|---|---|
| US37 Tasker không đến | `UPDATE Bookings SET ScheduledTime = DATEADD(MINUTE, -31, SYSUTCDATETIME()) WHERE Id = <id>;` (đơn đang 2 Accepted) | `isTaskerNoShow = true`, Tasker +1 điểm vi phạm |
| US34 Tự xác nhận sau 24 giờ | `UPDATE Bookings SET AutoConfirmAt = DATEADD(SECOND, -1, SYSUTCDATETIME()) WHERE Id = <id>;` (đơn 4 Completed) | Đơn → 5 Paid, ví Tasker được cộng tiền |
| US21 Quá 15 phút chưa thanh toán | `UPDATE Bookings SET PaymentDueAt = DATEADD(MINUTE, -1, SYSUTCDATETIME()) WHERE Id = <id>;` (đơn 10) | Đơn → 6 Cancelled |
| US33 Đơn chưa có Tasker quá 24 giờ | `UPDATE Bookings SET UpdatedAt = DATEADD(HOUR, -25, SYSUTCDATETIME()) WHERE Id = <id>;` (đơn 8) | Đơn → 9 Expired, hoàn 100% |
| Test riêng nhánh Sprint 2 (chưa có API thanh toán) | `UPDATE Bookings SET Status = 0 WHERE Id = <id>;` | Hệ thống bắt đầu ghép nối |

## 3. Cấu trúc thư mục

```
HomeService-Backend/
├── Controllers/   # 1 controller / 1 nhóm API (Auth, Bookings, Offers, Payments, Wallet, Disputes, Reviews, Notifications, Profile, Taskers, Users, Reports, Uploads, ServiceCategories)
├── DTOs/          # HỢP ĐỒNG với FE (SRS mục 9) – field mới chỉ thêm vào CUỐI
├── Models/        # Entity ánh xạ bảng (SRS mục 7)
├── Enums/         # Gửi/nhận dạng SỐ, chỉ thêm giá trị vào CUỐI (SRS 7.8)
├── Services/      # Nghiệp vụ: BookingService, MatchingService, OfferService, PaymentService, SettlementService,
│                  #   WalletService, CancellationService, DisputeService, TaskerDisciplineService, AuditService,
│                  #   BookingJobs + BookingWorker (tác vụ nền), NotificationService, AuthService, TokenService
├── Options/       # Tham số nghiệp vụ đọc từ appsettings.json (Business, Matching, Settlement, Notifications)
├── Common/        # ApiException, Roles, Money, GeoHelper (Haversine), SkillMatcher, VnTime...
├── Data/          # AppDbContext, DbInitializer (migrate + seed)
├── Migrations/    # 1 migration / Sprint
├── Middleware/    # Lỗi chung → JSON { "message" } (SRS 8.6)
├── wwwroot/uploads/  # ảnh tải lên qua POST /api/uploads (trả về "/uploads/…" tương đối)
└── docs/          # Postman collection, hướng dẫn Jira theo Sprint, đề xuất cập nhật SRS
```

## 4. Tham số nghiệp vụ (appsettings.json)

| Mục | Tham số chính |
|---|---|
| `Business` | `CommissionRate` 0.2 (chốt vào đơn lúc tạo) |
| `Matching` | `WaveRadiusKm` [3,5,8], `WaveTimeoutSeconds` [45,45,60], `OffersPerWave` 5, `LocationFreshMinutes` 60, `AdvanceRetryMinutes` 30, `StopRetryHoursBefore` 2, `TickSeconds` 5, `MaxRounds` 5, `ScheduleGapHours` 2 |
| `Settlement` | `PaymentTimeoutMinutes` 15, `AutoConfirmHours` 24, `ReminderHoursBefore` 2, `CashMinCompletedOrders` 3, `CashMaxOrderValue` 1.000.000, `LateCancelHours` 2, `LateCancelFeePercent` 20, `ArrivedCancelFeePercent` 50, `ArrivedDistanceMeters` 200, `DisputeHours` 24, `TaskerResponseHours` 24, `NoShowMinutes` 30, `NoShowAutoCancelHours` 2, `ViolationPointsToSuspend` 3, `SuspendDays` 7, `LowBalanceThreshold` 50.000 |
| `Notifications` | `AppointmentReminderMinutes` 60 |

## 5. API theo Sprint (đầy đủ ở Swagger)

| Sprint | User Story | Endpoint chính |
|---|---|---|
| 1 | US01–US04, US07, US10, US11 | `POST /auth/register`, `POST /auth/login`, `POST /auth/logout`, `POST/GET /taskers/profile`, `GET /taskers/pending-verification`, `PUT /taskers/{taskerProfileId}/verification`, `POST /uploads`, `GET/POST/PUT /servicecategories` |
| 2 | US12, US13, US16, US08 | `POST /bookings`, `GET /bookings/my`, `GET /bookings/{id}`, `PUT /taskers/availability`, `GET /offers/my` |
| 3 | US17, US21, US18, US34 | `POST /payments`, `POST /bookings/{id}/accept/reject`, `PUT /bookings/{id}/status`, `POST /bookings/{id}/confirm` |
| 4 | US22, US19, US33, US06, US28 | `PUT/GET /bookings/{id}/location`, `POST /bookings/{id}/retry`, `POST /bookings/{id}/cancel`, `GET /notifications/my`, `PUT /notifications/{id}/read`, `PUT /notifications/read-all` |
| 5 | US30, US25, US20, US14, US35 | `GET /users`, `PUT /users/{id}/active-status`, `POST /reviews`, `GET /bookings/{id}/cancellation-fee`, `POST /bookings/{id}/dispute`, `GET /disputes`, `PUT /disputes/{id}/respond/resolve` |
| 6 | US37, US15, US23, US24, US26, US27 | `GET /wallet`, `POST /wallet/topup/withdraw`, `GET /wallet/transactions`, `GET /payments/my`, `GET /reviews/user/{userId}`, `POST /reviews/{id}/report`, `GET /reviews/flagged`, `PUT /reviews/{id}/flag`, `DELETE /reviews/{id}` |
| 7 | US29, US05, US09, US31, US32, US36 | `GET/PUT /profile`, `DELETE /servicecategories/{id}`, `PUT /servicecategories/{id}/restore`, `GET /reports/revenue/bookings-summary/tasker-performance`, `POST /bookings/{id}/report-unpaid`, `PUT /users/{id}/cash-blocked` |

Các điểm khác/bổ sung so với SRS v1.3 (để nhóm cập nhật SRS v1.4): xem `docs/DE_XUAT_CAP_NHAT_SRS.md`.

## 6. Quy ước làm việc nhóm

- Nhánh theo Sprint/Story: `feature/sprint-N-...` hoặc `feature/USxx-mo-ta`; commit `feat: USxx ...`, `chore: ...`, `docs: ...`.
- Không code thẳng trên `main`; mở Pull Request và nhờ 1 bạn review trước khi merge.
- Đổi hợp đồng API (DTO, enum, endpoint) → cập nhật SRS trước, chỉ **thêm mới**, không đổi/xóa field đã phát hành.
- Thay đổi model → tạo migration mới: `dotnet ef migrations add <TenMigration>` (Backend tự áp dụng khi chạy).
