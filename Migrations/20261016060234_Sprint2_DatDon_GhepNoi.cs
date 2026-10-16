using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeService.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_DatDon_GhepNoi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_TaskerId",
                table: "Bookings");

            migrationBuilder.AddColumn<bool>(
                name: "IsCashBlocked",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingAmount",
                table: "Users",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UnpaidReportCount",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "LastLatitude",
                table: "TaskerProfiles",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLocationAt",
                table: "TaskerProfiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LastLongitude",
                table: "TaskerProfiles",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionRate",
                table: "Bookings",
                type: "decimal(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CurrentWave",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CustomerPresent",
                table: "Bookings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MatchRound",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextMatchAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDueAt",
                table: "Bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethod",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "Bookings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "BookingOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookingId = table.Column<int>(type: "int", nullable: false),
                    TaskerId = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    Wave = table.Column<int>(type: "int", nullable: false),
                    DistanceKm = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: false),
                    Score = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingOffers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingOffers_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingOffers_Users_TaskerId",
                        column: x => x.TaskerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_NextMatchAt",
                table: "Bookings",
                columns: new[] { "Status", "NextMatchAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TaskerId_Status",
                table: "Bookings",
                columns: new[] { "TaskerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BookingOffers_BookingId_TaskerId_Round",
                table: "BookingOffers",
                columns: new[] { "BookingId", "TaskerId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookingOffers_TaskerId_Status",
                table: "BookingOffers",
                columns: new[] { "TaskerId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingOffers");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_Status_NextMatchAt",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_TaskerId_Status",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "IsCashBlocked",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OutstandingAmount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UnpaidReportCount",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastLatitude",
                table: "TaskerProfiles");

            migrationBuilder.DropColumn(
                name: "LastLocationAt",
                table: "TaskerProfiles");

            migrationBuilder.DropColumn(
                name: "LastLongitude",
                table: "TaskerProfiles");

            migrationBuilder.DropColumn(
                name: "CommissionRate",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CurrentWave",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CustomerPresent",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "MatchRound",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "NextMatchAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentDueAt",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Bookings");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TaskerId",
                table: "Bookings",
                column: "TaskerId");
        }
    }
}
