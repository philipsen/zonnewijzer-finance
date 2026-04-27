using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SigenergyStatusSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Configured = table.Column<bool>(type: "INTEGER", nullable: false),
                    BatterySocPercent = table.Column<double>(type: "REAL", nullable: true),
                    BatteryPowerKw = table.Column<double>(type: "REAL", nullable: true),
                    SolarPowerKw = table.Column<double>(type: "REAL", nullable: true),
                    RemainingBatteryKwh = table.Column<double>(type: "REAL", nullable: true),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigenergyStatusSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmappeeStatusSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    RangeStartMs = table.Column<long>(type: "INTEGER", nullable: false),
                    RangeEndMsExclusive = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveSessionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedSessionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmappeeStatusSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SigenergyStatusSnapshots_FetchedAtUtc",
                table: "SigenergyStatusSnapshots",
                column: "FetchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SmappeeStatusSnapshots_FetchedAtUtc",
                table: "SmappeeStatusSnapshots",
                column: "FetchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SmappeeStatusSnapshots_ServiceLocationId_FetchedAtUtc",
                table: "SmappeeStatusSnapshots",
                columns: new[] { "ServiceLocationId", "FetchedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SigenergyStatusSnapshots");

            migrationBuilder.DropTable(
                name: "SmappeeStatusSnapshots");
        }
    }
}
