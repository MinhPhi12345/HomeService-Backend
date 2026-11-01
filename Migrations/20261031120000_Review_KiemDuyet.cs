using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeService.Api.Migrations
{
    /// <inheritdoc />
    /// <summary>US27 – lưu người báo cáo, lý do báo cáo và kết quả kiểm duyệt đánh giá.</summary>
    public partial class Review_KiemDuyet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ModeratedAt",
                table: "Reviews",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModeratedById",
                table: "Reviews",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModerationNote",
                table: "Reviews",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportReason",
                table: "Reviews",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportedAt",
                table: "Reviews",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportedById",
                table: "Reviews",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModeratedAt",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ModeratedById",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ModerationNote",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ReportReason",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ReportedAt",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "ReportedById",
                table: "Reviews");
        }
    }
}
