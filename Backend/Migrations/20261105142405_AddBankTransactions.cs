using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddBankTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AccountIban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CounterpartyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CounterpartyBic = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                    CounterpartyIban = table.Column<string>(type: "TEXT", maxLength: 34, nullable: true),
                    TransactionCode = table.Column<string>(type: "TEXT", maxLength: 8, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: true),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReviewStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceBankMatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BankTransactionId = table.Column<int>(type: "INTEGER", nullable: false),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    MatchKind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceBankMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceBankMatches_BankTransactions_BankTransactionId",
                        column: x => x.BankTransactionId,
                        principalTable: "BankTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceBankMatches_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_Fingerprint",
                table: "BankTransactions",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBankMatches_AppUserId_Year_Month",
                table: "InvoiceBankMatches",
                columns: new[] { "AppUserId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceBankMatches_BankTransactionId",
                table: "InvoiceBankMatches",
                column: "BankTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceBankMatches");

            migrationBuilder.DropTable(
                name: "BankTransactions");
        }
    }
}
