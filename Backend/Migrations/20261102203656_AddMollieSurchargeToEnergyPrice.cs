using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMollieSurchargeToEnergyPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MollieSurchargeEur",
                table: "EnergyPrices",
                type: "TEXT",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0.32m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MollieSurchargeEur",
                table: "EnergyPrices");
        }
    }
}
