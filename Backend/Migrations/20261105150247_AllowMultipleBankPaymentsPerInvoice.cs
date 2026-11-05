using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AllowMultipleBankPaymentsPerInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceBankMatches_AppUserId_Year_Month",
                table: "InvoiceBankMatches");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBankMatches_AppUserId_Year_Month",
                table: "InvoiceBankMatches",
                columns: new[] { "AppUserId", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceBankMatches_AppUserId_Year_Month",
                table: "InvoiceBankMatches");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBankMatches_AppUserId_Year_Month",
                table: "InvoiceBankMatches",
                columns: new[] { "AppUserId", "Year", "Month" },
                unique: true);
        }
    }
}
