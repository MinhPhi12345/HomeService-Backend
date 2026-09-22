# HomeService – Backend API

Backend dùng chung cho toàn bộ hệ thống **HomeService** (website khách hàng, website admin, app di động) — đề tài Công nghệ phần mềm Nâng cao, Nhóm 01.

- **Framework**: ASP.NET Core 8 Web API
- **ORM**: Entity Framework Core (Code First)
- **Database**: SQL Server
- **Auth**: JWT Bearer
- **Docs**: Swagger / OpenAPI

## Cấu trúc thư mục

```
HomeService-Backend/
├── Controllers/        # API endpoints (Auth, Bookings, ServiceCategories, Taskers, Users, Reviews, Reports)
├── Models/              # Entity: User, TaskerProfile, ServiceCategory, Booking, Payment, Review, Notification
├── Enums/                # UserRole, BookingStatus, TaskerVerificationStatus, PaymentStatus...
├── DTOs/                 # Request/Response models
├── Data/                 # AppDbContext (EF Core)
├── Services/             # Business logic (AuthService, TokenService, ...)
├── Middleware/           # Exception handling
├── Migrations/           # EF Core migrations (sinh ra khi chạy lệnh add-migration)
├── docker-compose.yml    # SQL Server + API
├── Dockerfile
└── Program.cs
```

Mỗi Model/Controller đều có comment gắn với Epic/User Story tương ứng trong file **Nhom 01 – Backlog** (vd: `// US12/US13`) để dễ đối chiếu khi phân công task trên Jira.

## Yêu cầu cài đặt

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (local, hoặc chạy qua Docker — xem bên dưới)
- (Tùy chọn) Docker Desktop nếu muốn chạy bằng docker-compose

## Chạy local (không dùng Docker)

1. Cài SQL Server (Developer edition) hoặc dùng SQL Server container riêng.
2. Cập nhật chuỗi kết nối trong `appsettings.Development.json` hoặc dùng `dotnet user-secrets`:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=HomeServiceDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;"
   ```
3. Khôi phục package và tạo database từ migrations:
   ```bash
   dotnet restore
   dotnet tool install --global dotnet-ef   # nếu chưa có
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   ```
4. Chạy API:
   ```bash
   dotnet run
   ```
5. Mở Swagger UI tại `https://localhost:xxxx/swagger` để test API.

## Chạy bằng Docker (khuyến nghị cho cả nhóm dùng chung)

```bash
cp .env.example .env   # chỉnh SA_PASSWORD, JWT_KEY nếu cần
docker compose up --build
```

- API: `http://localhost:8080`
- SQL Server: `localhost:1433` (user `sa`, password theo `.env`)

Sau khi container SQL Server chạy, vào container `api` để apply migrations lần đầu:
```bash
docker compose exec api dotnet ef database update
```
(Cần cài `dotnet-ef` trong image nếu muốn chạy migration trực tiếp trong container; cách đơn giản hơn khi mới bắt đầu là chạy `dotnet ef database update` từ máy local, trỏ connection string vào `localhost,1433`.)

## Quy ước code cho nhóm

- Mỗi Controller action nên gắn với đúng 1 User Story trong Backlog (comment `// USxx` ở đầu action).
- Tên nhánh git theo Sprint/Story, ví dụ: `feature/US12-tao-don-dat-dich-vu`.
- Trước khi merge vào `main`, đảm bảo `dotnet build` không lỗi và API chạy được qua Swagger.

## Liên kết

- File phân tích yêu cầu & Backlog: `Nhom 01 – Backlog.docx`
- Repo liên quan: `HomeService-Web-Customer`, `HomeService-Web-Admin`, `HomeService-Mobile`
