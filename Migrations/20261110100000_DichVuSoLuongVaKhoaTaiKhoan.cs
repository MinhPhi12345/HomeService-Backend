using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeService.Api.Migrations
{
    /// <inheritdoc />
    public partial class DichVuSoLuongVaKhoaTaiKhoan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockDetail",
                table: "Users",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockPendingAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LockReason",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LockedById",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerPrepares",
                table: "ServiceCategories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Excludes",
                table: "ServiceCategories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Includes",
                table: "ServiceCategories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxQuantity",
                table: "ServiceCategories",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<int>(
                name: "MinQuantity",
                table: "ServiceCategories",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "MinutesPerUnit",
                table: "ServiceCategories",
                type: "int",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "ServiceCategories",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityHint",
                table: "ServiceCategories",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantityLabel",
                table: "ServiceCategories",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresItemList",
                table: "ServiceCategories",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "EstimatedMinutes",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 120);

            migrationBuilder.CreateTable(
                name: "AccountAppeals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LockedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AttachmentUrls = table.Column<string>(type: "nvarchar(1600)", maxLength: 1600, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AdminNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedById = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountAppeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountAppeals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingItems_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookingPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    ItemNo = table.Column<int>(type: "int", nullable: true),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingPhotos_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountAppeals_Status",
                table: "AccountAppeals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AccountAppeals_UserId_CreatedAt",
                table: "AccountAppeals",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_BookingId_ItemNo",
                table: "BookingItems",
                columns: new[] { "BookingId", "ItemNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingPhotos_BookingId_Stage",
                table: "BookingPhotos",
                columns: new[] { "BookingId", "Stage" });

            // SRS v1.8 – cấu hình số lượng, thời lượng, hạng mục & chi tiết cho 5 dịch vụ mẫu (theo tên)
            migrationBuilder.Sql(@"UPDATE [ServiceCategories] SET [Unit] = N'giờ', [MinQuantity] = 2, [MaxQuantity] = 4, [QuantityLabel] = N'Số giờ làm', [QuantityHint] = N'2 giờ ≈ 55 m² (2 phòng) · 3 giờ ≈ 85 m² (3 phòng) · 4 giờ ≈ 105 m² (4 phòng)', [MinutesPerUnit] = 60, [RequiresItemList] = 0, [Includes] = N'Quét, hút bụi và lau sàn các phòng' + NCHAR(10) + N'Lau bụi bàn ghế, kệ tủ, cửa sổ trong tầm với' + NCHAR(10) + N'Vệ sinh bếp: mặt bếp, bồn rửa, mặt ngoài tủ bếp' + NCHAR(10) + N'Vệ sinh nhà tắm: bồn cầu, lavabo, gương, sàn' + NCHAR(10) + N'Thu gom và đổ rác', [Excludes] = N'Giặt ủi quần áo, nấu ăn (đặt dịch vụ riêng)' + NCHAR(10) + N'Lau kính mặt ngoài tòa nhà, làm việc trên cao trên 2 m' + NCHAR(10) + N'Giặt sofa, nệm, rèm bằng máy chuyên dụng' + NCHAR(10) + N'Dọn nhà sau xây dựng, sửa chữa', [CustomerPrepares] = N'Chọn số giờ phù hợp với diện tích nhà' + NCHAR(10) + N'Chổi, cây lau nhà, khăn và nước lau sàn' + NCHAR(10) + N'Cất giữ đồ quý giá, giấy tờ quan trọng', [Notes] = N'Tasker làm đúng số giờ đã đặt; cần thêm giờ thì đặt đơn mới' + NCHAR(10) + N'Nhà trên 105 m² nên đặt 2 đơn vào các buổi khác nhau' WHERE [Name] = N'Dọn dẹp nhà';");
            migrationBuilder.Sql(@"UPDATE [ServiceCategories] SET [Unit] = N'mẻ', [MinQuantity] = 1, [MaxQuantity] = 3, [QuantityLabel] = N'Số mẻ giặt', [QuantityHint] = N'1 mẻ ≈ 7 kg ≈ 15–20 bộ quần áo (máy giặt gia đình)', [MinutesPerUnit] = 90, [RequiresItemList] = 0, [Includes] = N'Phân loại quần áo theo màu và chất liệu' + NCHAR(10) + N'Giặt bằng máy giặt của gia đình, phơi hoặc sấy khô' + NCHAR(10) + N'Ủi phẳng và gấp gọn quần áo', [Excludes] = N'Giặt khô, giặt hấp đồ cao cấp (vest, áo dài, đồ da)' + NCHAR(10) + N'Giặt chăn, nệm, rèm cửa cỡ lớn' + NCHAR(10) + N'Tẩy vết bẩn lâu ngày bằng hóa chất chuyên dụng', [CustomerPrepares] = N'Máy giặt, nước giặt, nước xả và bàn ủi' + NCHAR(10) + N'Ước lượng số mẻ: 1 mẻ ≈ 7 kg ≈ 15–20 bộ quần áo', [Notes] = N'Nhiều hơn 3 mẻ (khoảng 60 bộ quần áo) thì đặt thêm đơn vào ngày khác' WHERE [Name] = N'Giặt ủi';");
            migrationBuilder.Sql(@"UPDATE [ServiceCategories] SET [Unit] = N'hạng mục', [MinQuantity] = 1, [MaxQuantity] = 5, [QuantityLabel] = N'Số hạng mục cần sửa', [QuantityHint] = N'Mỗi hạng mục là một chỗ cần sửa, ví dụ: 1 vòi nước rỉ, 1 ổ điện hỏng.', [MinutesPerUnit] = 60, [RequiresItemList] = 1, [Includes] = N'Kiểm tra và sửa vòi nước, đường ống nước lộ thiên bị rò rỉ' + NCHAR(10) + N'Thông tắc bồn rửa, lavabo, thoát sàn đơn giản' + NCHAR(10) + N'Sửa, thay ổ cắm, công tắc, bóng đèn, máng đèn' + NCHAR(10) + N'Kiểm tra chập điện, mất điện cục bộ trong nhà', [Excludes] = N'Tiền linh kiện, vật tư thay thế (khách trả riêng theo giá thực tế)' + NCHAR(10) + N'Đi lại đường điện, ống nước âm tường' + NCHAR(10) + N'Sửa thiết bị điện tử: tivi, tủ lạnh, máy giặt, máy lạnh', [CustomerPrepares] = N'Ghi rõ từng hạng mục cần sửa khi đặt đơn' + NCHAR(10) + N'Ngắt cầu dao nếu đang có sự cố chập điện', [Notes] = N'Tasker chụp ảnh trước và sau cho từng hạng mục, bạn kiểm tra ảnh trước khi xác nhận hoàn thành' + NCHAR(10) + N'Giá là tiền công cho mỗi hạng mục' WHERE [Name] = N'Sửa điện nước';");
            migrationBuilder.Sql(@"UPDATE [ServiceCategories] SET [Unit] = N'buổi', [MinQuantity] = 1, [MaxQuantity] = 1, [QuantityLabel] = N'Số buổi', [QuantityHint] = NULL, [MinutesPerUnit] = 90, [RequiresItemList] = 0, [Includes] = N'Tưới cây, nhổ cỏ, xới đất' + NCHAR(10) + N'Cắt tỉa cành lá hư, tạo dáng cơ bản' + NCHAR(10) + N'Bón phân, phun thuốc phòng sâu bệnh', [Excludes] = N'Tiền phân bón, thuốc, chậu và đất (khách chuẩn bị)' + NCHAR(10) + N'Chặt hạ cây lớn, cây cao trên 3 m' + NCHAR(10) + N'Thiết kế, thi công sân vườn mới', [CustomerPrepares] = N'Phân bón, thuốc, chậu và đất (nếu cần thay)' + NCHAR(10) + N'Vòi nước hoặc bình tưới', [Notes] = N'Phù hợp ban công, sân thượng và vườn nhỏ dưới 50 m²' WHERE [Name] = N'Chăm sóc cây';");
            migrationBuilder.Sql(@"UPDATE [ServiceCategories] SET [Unit] = N'buổi', [MinQuantity] = 1, [MaxQuantity] = 1, [QuantityLabel] = N'Số buổi', [QuantityHint] = NULL, [MinutesPerUnit] = 120, [RequiresItemList] = 0, [Includes] = N'Đi chợ theo thực đơn và tiền chợ bạn đưa (khoảng 30 phút)' + NCHAR(10) + N'Sơ chế, nấu bữa ăn 3–4 món cho gia đình 2–6 người' + NCHAR(10) + N'Rửa dụng cụ, dọn gọn bếp sau khi nấu', [Excludes] = N'Tiền mua thực phẩm (khách trả riêng)' + NCHAR(10) + N'Nấu tiệc, cỗ trên 6 người hoặc trên 4 món', [CustomerPrepares] = N'Thực đơn mong muốn và tiền đi chợ (nếu nhờ Tasker đi chợ)' + NCHAR(10) + N'Dụng cụ nấu, gia vị cơ bản', [Notes] = N'Ghi khẩu vị, món kiêng hoặc dị ứng thực phẩm trong ô Ghi chú' WHERE [Name] = N'Nấu ăn gia đình';");
            // Đơn cũ: dịch vụ tính theo giờ → thời lượng = số giờ; còn lại giữ 120 phút (tương đương quy tắc cũ 2 giờ)
            migrationBuilder.Sql(@"UPDATE b SET b.[EstimatedMinutes] = b.[Quantity] * 60 FROM [Bookings] b INNER JOIN [ServiceCategories] s ON s.[Id] = b.[ServiceCategoryId] WHERE s.[Unit] = N'giờ';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountAppeals");

            migrationBuilder.DropTable(
                name: "BookingItems");

            migrationBuilder.DropTable(
                name: "BookingPhotos");

            migrationBuilder.DropColumn(
                name: "LockDetail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockPendingAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedById",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CustomerPrepares",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "Excludes",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "Includes",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "MaxQuantity",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "MinQuantity",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "MinutesPerUnit",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "QuantityHint",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "QuantityLabel",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "RequiresItemList",
                table: "ServiceCategories");

            migrationBuilder.DropColumn(
                name: "EstimatedMinutes",
                table: "Bookings");
        }
    }
}
