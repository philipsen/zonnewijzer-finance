using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class SplitBankPaymentsAcrossInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceBankMatches_BankTransactionId",
                table: "InvoiceBankMatches");

            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "InvoiceBankMatches",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE InvoiceBankMatches
                SET Amount = (
                    SELECT BankTransactions.Amount
                    FROM BankTransactions
                    WHERE BankTransactions.Id = InvoiceBankMatches.BankTransactionId
                )
                """);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBankMatches_BankTransactionId",
                table: "InvoiceBankMatches",
                column: "BankTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InvoiceBankMatches_BankTransactionId",
                table: "InvoiceBankMatches");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "InvoiceBankMatches");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBankMatches_BankTransactionId",
                table: "InvoiceBankMatches",
                column: "BankTransactionId",
                unique: true);
        }
    }
}
