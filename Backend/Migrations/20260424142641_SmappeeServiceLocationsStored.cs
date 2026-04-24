using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class SmappeeServiceLocationsStored : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmappeeServiceLocationDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServiceLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceSerialNumber = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ServiceLocationUuid = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmappeeServiceLocationDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmappeeServiceLocations",
                columns: table => new
                {
                    ServiceLocationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    ElectricityCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true),
                    ElectricityCurrency = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Timezone = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    AppliancesJson = table.Column<string>(type: "TEXT", nullable: true),
                    InfoFetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmappeeServiceLocations", x => x.ServiceLocationId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmappeeServiceLocationDevices_ServiceLocationId_DeviceSerialNumber",
                table: "SmappeeServiceLocationDevices",
                columns: new[] { "ServiceLocationId", "DeviceSerialNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmappeeServiceLocationDevices");

            migrationBuilder.DropTable(
                name: "SmappeeServiceLocations");
        }
    }
}
