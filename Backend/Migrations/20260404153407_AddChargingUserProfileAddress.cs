using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddChargingUserProfileAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HouseNumber",
                table: "ChargingUserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlaceName",
                table: "ChargingUserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "ChargingUserProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "ChargingUserProfiles",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HouseNumber",
                table: "ChargingUserProfiles");

            migrationBuilder.DropColumn(
                name: "PlaceName",
                table: "ChargingUserProfiles");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "ChargingUserProfiles");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "ChargingUserProfiles");
        }
    }
}
