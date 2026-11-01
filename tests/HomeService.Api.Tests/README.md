# HomeService.Api.Tests – Unit test cho Backend

Bộ test tự động cho `HomeService-Backend`, viết bằng **xUnit**, chạy trên **SQLite trong bộ nhớ**
(không cần SQL Server, không cần mạng). Mỗi test có một database riêng nên chạy độc lập và song song được.

- 354 hàm test / 446 test case (tính cả từng dòng `InlineData` của `[Theory]`)
- Phủ: `Common`, `Extensions`, `Options`, DTO validation, toàn bộ `Services`, toàn bộ `Controllers`,
  `Middleware`, `Data` (AppDbContext, DbInitializer)

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `Infrastructure/` | `TestDb` (SQLite in-memory), `TestBase` (lắp service thật), `Seed` (dữ liệu mẫu), giả lập user đăng nhập, giả lập OpenStreetMap |
| `Common/` | Hàm tiện ích (tiền, giờ VN, khoảng cách, kỹ năng, link ảnh…) và quy tắc kiểm tra dữ liệu DTO (SRS 6.1) |
| `Services/` | Nghiệp vụ: đăng ký/đăng nhập, JWT, tạo đơn, ghép nối theo đợt, nhận/từ chối, thanh toán & tạm giữ, giải ngân, hủy & phí hủy, khiếu nại, ví, điểm vi phạm, tác vụ nền |
| `Controllers/` | Mã HTTP, phân quyền theo người xem, lọc/phân trang, báo cáo, upload ảnh, bản đồ |
| `Data/` | Ràng buộc CSDL (unique, check, concurrency token), khởi tạo dữ liệu, middleware lỗi JSON |

## Chạy test

Mở terminal tại thư mục `HomeService-Backend`:

```powershell
dotnet test tests/HomeService.Api.Tests
```

Trong Visual Studio: thêm project vào solution (chuột phải Solution → Add → Existing Project →
`tests/HomeService.Api.Tests/HomeService.Api.Tests.csproj`), rồi mở **Test → Test Explorer → Run All**.

Chạy một nhóm test: `dotnet test tests/HomeService.Api.Tests --filter "FullyQualifiedName~MatchingServiceTests"`

## Đo độ bao phủ (code coverage)

```powershell
dotnet test tests/HomeService.Api.Tests --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet tool install -g dotnet-reportgenerator-globaltool   # chỉ cài 1 lần
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:coverage-report -reporttypes:Html -classfilters:"-HomeService.Api.Migrations.*"
start coverage-report/index.html
```

`TestResults/` và `coverage-report/` đã có trong `.gitignore`.

## Quy ước khi viết thêm test

- Kế thừa `TestBase` để có sẵn `Db`, `Fresh()` (đọc lại dữ liệu thật đã lưu) và các hàm `Create...()` tạo service.
- Tạo dữ liệu bằng `Seed` (`Db.Customer()`, `Db.Tasker()`, `Db.Service()`, `Db.Booking(...)`, `Db.HeldPayment(...)`).
- Gọi controller với người dùng giả: `new XController(...).AsUser(userId, UserRole.Customer)`.
- Tên test: `HamDuocTest_TinhHuong_KetQuaMongDoi` (tiếng Việt không dấu).
