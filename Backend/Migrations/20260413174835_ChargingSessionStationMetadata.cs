using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class ChargingSessionStationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChargingStationName",
                table: "ChargingSessions",
                type: "TEXT",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Connector",
                table: "ChargingSessions",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StationSerialNumber",
                table: "ChargingSessions",
                type: "TEXT",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargingStationName",
                table: "ChargingSessions");

            migrationBuilder.DropColumn(
                name: "Connector",
                table: "ChargingSessions");

            migrationBuilder.DropColumn(
                name: "StationSerialNumber",
                table: "ChargingSessions");
        }
    }
}
